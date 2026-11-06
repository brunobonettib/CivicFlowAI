using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

if (args.FirstOrDefault() == "verifier")
{
    await VerifierEvaluation.RunAsync();
    return;
}

if (args.FirstOrDefault() == "rag")
{
    await RagEvaluation.RunAsync(args.Skip(1).ToArray());
    return;
}

if (args.FirstOrDefault() == "evidence-summary")
{
    EvidenceEvaluation.Summarize(args.Length > 1 ? args[1] : "evaluation/evidence-results.json");
    return;
}
if (args.FirstOrDefault() == "evidence")
{
    await EvidenceEvaluation.RunAsync(args.Length > 1 ? args[1] : "evaluation/evidence-results.json");
    return;
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var questionJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "questions.json"));
var questions = JsonSerializer.Deserialize<EvaluationQuestion[]>(questionJson, jsonOptions)!;
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
    .AddEnvironmentVariables().Build();
var options = configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
options.Validate();
var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
var documentIds = knowledge.Documents.Select(document => document.Metadata.Id).ToHashSet();

// Rótulos definidos antes de gerar scores; casos relacionados não são fontes suficientes.
if (questions.Length != 15 || questions.Select(q => q.Id).Distinct().Count() != 15
    || questions.Count(q => q.Category == "dominio") != 9
    || questions.Count(q => q.Category == "ambigua") != 3
    || questions.Count(q => q.Category == "fora") != 3
    || questions.Any(q => string.IsNullOrWhiteSpace(q.Question)
        || (q.Category == "dominio") != (q.ExpectedDocumentIds.Length > 0)
        || q.ExpectedDocumentIds.Concat(q.RelatedDocumentIds).Any(id => !documentIds.Contains(id)))
    || documentIds.Any(id => questions.Count(q => q.ExpectedDocumentIds.Contains(id)) != 3))
    throw new InvalidDataException("Conjunto de avaliação inválido: esperados 9/3/3 casos e 3 por documento.");

using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
using var generator = new OllamaApiClient(http, options.Model);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var version = await generator.GetVersionAsync(cancellation.Token);
var modelName = options.Model.Contains(':') ? options.Model : options.Model + ":latest";
var model = (await generator.ListLocalModelsAsync(cancellation.Token)).Single(m => m.Name == modelName);
var chunkOptions = configuration.GetSection("Chunking").Get<ChunkingOptions>()!;
var chunks = knowledge.Documents.SelectMany(new MarkdownChunker(chunkOptions).Split).ToArray();
using var indexer = new EmbeddingIndexer(generator, chunks, options);
var indexing = await indexer.IndexAsync(cancellation.Token);
if (indexing.Status != IndexingStatus.Completed) throw new InvalidOperationException(indexing.Error);
var snapshot = indexer.Current;
var search = new SemanticSearchService(indexer, generator, new() { TopK = 2 }, options);
var rows = new List<EvaluationRow>();
foreach (var question in questions)
{
    var result = await search.SearchAsync(question.Question, cancellation.Token);
    if (!ReferenceEquals(snapshot, indexer.Current)) throw new InvalidOperationException("O índice foi alterado durante a avaliação.");
    rows.Add(new(question.Id, result.Status.ToString(), result.Results.Select(hit =>
        new EvaluationHit(hit.Chunk.DocumentoId, hit.Score)).ToArray(), result.Error));
}

var eligible = questions.Where(q => q.ExpectedDocumentIds.Length > 0).ToArray();
var correct = eligible.Count(question => question.ExpectedDocumentIds.Contains(
    rows.Single(row => row.Id == question.Id).TopResults.FirstOrDefault()?.DocumentId));
var report = new
{
    ollamaVersion = version, model = model.Name, modelDigest = model.Digest,
    questionsSha256 = Hash(questionJson),
    corpusSha256 = Hash(string.Join("\n", knowledge.Documents.Select(document => JsonSerializer.Serialize(document)))),
    chunkOptions.TargetCharacters, options.BatchSize, topK = 2,
    documents = knowledge.Documents.Count, chunks = snapshot.Items.Count, dimensions = snapshot.Dimensions,
    expectedSourceCases = eligible.Length, top1Correct = correct,
    top1Accuracy = (double)correct / eligible.Length,
    failures = rows.Count(row => row.Status != nameof(SearchStatus.Completed)),
    results = rows
};
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "evaluation/results.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
Console.WriteLine($"Avaliação: {rows.Count} buscas; Top 1 = {correct}/{eligible.Length}; falhas operacionais = {report.failures}; {snapshot.Items.Count} chunks; {snapshot.Dimensions} dimensões.");
if (report.failures > 0) Environment.ExitCode = 1;

static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

internal sealed record EvaluationQuestion(string Id, string Category, string Question,
    string[] ExpectedDocumentIds, string[] RelatedDocumentIds, string Evidence, string Note);
internal sealed record EvaluationHit(string DocumentId, double Score);
internal sealed record EvaluationRow(string Id, string Status, EvaluationHit[] TopResults, string? Error);
