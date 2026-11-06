using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

internal static class RagEvaluation
{
    public static async Task RunAsync(string[] args)
    {
        var baseline = args.Contains("--baseline");
        var output = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "evaluation/rag-task08-results.json";
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        using var old = JsonDocument.Parse(File.ReadAllText("evaluation/questions.json"));
        var cases = old.RootElement.EnumerateArray().Select(q => new Case(q.GetProperty("id").GetString()!,
            q.GetProperty("question").GetString()!, q.GetProperty("evidence").GetString() switch
            { "suficiente" => "SUPPORTED", "esclarecimento" => "NEEDS_CLARIFICATION", _ => "UNSUPPORTED" },
            q.GetProperty("expectedDocumentIds").EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                ? q.GetProperty("expectedDocumentIds")[0].GetString() : null)).ToList();
        if (baseline) cases = cases.Where(q => new[] { "D01", "D04", "A01", "A02", "F01", "F02", "F03" }.Contains(q.Id)).ToList();
        else cases.AddRange(JsonSerializer.Deserialize<Case[]>(File.ReadAllText("evaluation/rag-unseen.json"), json)!);
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).AddEnvironmentVariables().Build();
        var embedding = config.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
        var generation = config.GetSection("Generation").Get<GenerationOptions>()!;
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var chunks = knowledge.Documents.SelectMany(new MarkdownChunker(config.GetSection("Chunking").Get<ChunkingOptions>()!).Split).ToArray();
        using var generator = new OllamaApiClient(new Uri(embedding.BaseUrl), embedding.Model);
        using var chat = new Recorder(new OllamaApiClient(new Uri(generation.BaseUrl), generation.Model));
        using var index = new EmbeddingIndexer(generator, chunks, embedding);
        var search = new SemanticSearchService(index, generator, config.GetSection("SemanticSearch").Get<SemanticSearchOptions>()!, embedding);
        using var rag = new RagService(index, new(search, chat, generation), chat, knowledge, chunks, generation);
        var rows = new List<object>();
        foreach (var q in cases)
        {
            chat.Calls.Clear();
            var result = await rag.ConsultAsync(q.Question);
            var response = result.Response;
            var valid = response is not null && response.Fontes.All(s => knowledge.Documents.Any(d =>
                d.Metadata.Id == s.Id && d.Metadata.Url == s.Url && d.Metadata.Titulo == s.Titulo && d.Metadata.DataConsulta == s.DataConsulta))
                && response.Trechos.All(t => response.Fontes.Any(s => s.Id == t.FonteId)
                    && chunks.Any(c => c.DocumentoId == t.FonteId && c.Conteudo.Contains(t.Texto))
                    && response.Resposta.Contains(t.Texto));
            rows.Add(new { q.Id, q.Question, q.Expected, actual = response?.Status, result.StatusCode, referencesValid = valid,
                expectedSourcePresent = q.Source is null || response?.Fontes.Any(s => s.Id == q.Source) == true,
                response, stages = chat.Calls.ToArray() });
            File.WriteAllText(output, JsonSerializer.Serialize(new { date = "2026-11-06", embedding.Model,
                generationModel = generation.Model, topK = config["SemanticSearch:TopK"], results = rows }, json));
            Console.WriteLine($"{q.Id}: {response?.Status ?? result.StatusCode.ToString()} (esperado {q.Expected})");
        }
    }
    private sealed record Case(string Id, string Question, string Expected, string? Source);
    private sealed class Recorder(IChatClient inner) : DelegatingChatClient(inner)
    {
        public List<object> Calls { get; } = [];
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var input = messages.ToArray();
            var response = await base.GetResponseAsync(input, options, cancellationToken);
            using var payload = JsonDocument.Parse(input.Single(m => m.Role == ChatRole.User).Text);
            var evidenceStage = payload.RootElement.TryGetProperty("chunks", out var evidence);
            var documents = evidenceStage ? evidence.EnumerateArray().Select(e => e.GetProperty("url").GetString()).ToArray() : [];
            Calls.Add(new { stage = evidenceStage ? "evidence" : "coverage", documents, raw = response.Text,
                promptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Single(m => m.Role == ChatRole.System).Text))) });
            return response;
        }
    }
}
