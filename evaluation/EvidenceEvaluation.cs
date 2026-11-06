using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

internal static class EvidenceEvaluation
{
    public static async Task RunAsync(string outputPath)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true, Converters = { new JsonStringEnumConverter() }
        };
        var questionJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "questions.json"));
        var questions = JsonSerializer.Deserialize<EvaluationQuestion[]>(questionJson, jsonOptions)!;
        if (questions.Length != 15 || questions.Select(q => q.Id).Distinct().Count() != 15)
            throw new InvalidDataException("Esperados os 15 casos de avaliação.");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddEnvironmentVariables().Build();
        var embeddingOptions = configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
        var generationOptions = configuration.GetSection("Generation").Get<GenerationOptions>()!;
        var chunkOptions = configuration.GetSection("Chunking").Get<ChunkingOptions>()!;
        var searchOptions = configuration.GetSection("SemanticSearch").Get<SemanticSearchOptions>()!;
        generationOptions.Validate();
        using var generationHttp = new HttpClient { BaseAddress = new Uri(generationOptions.BaseUrl), Timeout = TimeSpan.FromSeconds(generationOptions.TimeoutSeconds) };
        using var ollama = new OllamaApiClient(generationHttp, generationOptions.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(35));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var version = await ollama.GetVersionAsync(cancellation.Token);
        var modelName = generationOptions.Model.Contains(':') ? generationOptions.Model : generationOptions.Model + ":latest";
        var model = (await ollama.ListLocalModelsAsync(cancellation.Token)).Single(m => m.Name == modelName);
        using var embeddingHttp = new HttpClient { BaseAddress = new Uri(embeddingOptions.BaseUrl), Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds) };
        using var generator = new OllamaApiClient(embeddingHttp, embeddingOptions.Model);
        var embeddingName = embeddingOptions.Model.Contains(':') ? embeddingOptions.Model : embeddingOptions.Model + ":latest";
        var embeddingModel = (await generator.ListLocalModelsAsync(cancellation.Token)).Single(m => m.Name == embeddingName);
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var chunks = knowledge.Documents.SelectMany(new MarkdownChunker(chunkOptions).Split).ToArray();
        using var indexer = new EmbeddingIndexer(generator, chunks, embeddingOptions);
        var indexing = await indexer.IndexAsync(cancellation.Token);
        if (indexing.Status != IndexingStatus.Completed) throw new InvalidOperationException(indexing.Error);
        var snapshot = indexer.Current;
        var search = new SemanticSearchService(indexer, generator, searchOptions, embeddingOptions);
        using var recorder = new RecordingChatClient(ollama);
        var service = new EvidenceAnalysisService(search, recorder, generationOptions);
        var rows = new List<EvidenceRow>();

        foreach (var question in questions)
        {
            var expected = question.Evidence switch
            {
                "suficiente" => EvidenceStatus.SUPPORTED,
                "esclarecimento" => EvidenceStatus.NEEDS_CLARIFICATION,
                _ => EvidenceStatus.UNSUPPORTED
            };
            recorder.Reset();
            var result = await service.AnalyzeAsync(question.Question, cancellation.Token);
            if (!ReferenceEquals(snapshot, indexer.Current)) throw new InvalidOperationException("Índice alterado durante a avaliação.");
            var retrieved = Array.Empty<RetrievedEvidence>();
            if (recorder.Payload is not null)
            {
                using var payload = JsonDocument.Parse(recorder.Payload);
                retrieved = payload.RootElement.GetProperty("chunks").EnumerateArray().Select(c =>
                {
                    // Auditoria por igualdade exata dos dados enviados; não repara referências do modelo.
                    var original = chunks.Single(chunk => chunk.Conteudo == c.GetProperty("content").GetString()
                        && chunk.Titulo == c.GetProperty("title").GetString()
                        && chunk.Url.AbsoluteUri == c.GetProperty("url").GetString()
                        && chunk.Secao == c.GetProperty("section").GetString());
                    return new RetrievedEvidence(original.Id, original.DocumentoId, c.GetProperty("id").GetString()!);
                }).ToArray();
            }
            var returnedIds = ExtractIds(recorder.ResponseText);
            var idsValid = returnedIds is null ? (bool?)null : returnedIds.All(id => retrieved.Any(chunk => chunk.Alias == id));
            var citedDocuments = (result.Evidence ?? []).Select(c => c.DocumentoId).Distinct().ToArray();
            rows.Add(new(question.Id, expected, result.Outcome, result.Assessment?.Status,
                result.Assessment?.Justification, result.Assessment?.ClarificationQuestion,
                retrieved, returnedIds, idsValid, citedDocuments,
                question.ExpectedDocumentIds.Length == 0 ? null : question.ExpectedDocumentIds.All(citedDocuments.Contains),
                recorder.ResponseText, result.Error, result.Assessment?.EvidenceIds.ToArray()));
            Console.WriteLine($"{question.Id}: {result.Outcome}; esperado={expected}; obtido={result.Assessment?.Status}");
        }

        var statuses = Enum.GetValues<EvidenceStatus>();
        var completed = rows.Count(row => row.Outcome == EvidenceOutcome.Completed);
        var correct = rows.Count(row => row.Outcome == EvidenceOutcome.Completed && row.Expected == row.Predicted);
        var falseSupported = rows.Where(row => row.Expected != EvidenceStatus.SUPPORTED && row.Predicted == EvidenceStatus.SUPPORTED).Select(row => row.Id).ToArray();
        var report = new
        {
            ollamaVersion = version, generationModel = model.Name, generationDigest = model.Digest,
            embeddingModel = embeddingModel.Name, embeddingDigest = embeddingModel.Digest,
            recorder.Temperature, recorder.MaxOutputTokens, recorder.PromptSha256,
            questionsSha256 = Hash(questionJson), corpusSha256 = Hash(JsonSerializer.Serialize(knowledge.Documents)),
            searchOptions.TopK, chunks = snapshot.Items.Count, dimensions = snapshot.Dimensions,
            cases = rows.Count, completed, correct, accuracy = (double)correct / rows.Count,
            technicalFailures = rows.Count - completed, falseSupported,
            supportedWithMissingExpectedSource = rows.Where(r => r.Predicted == EvidenceStatus.SUPPORTED && r.ExpectedSourcesCited == false).Select(r => r.Id).ToArray(),
            // Linhas = esperado; colunas = predito. Falhas técnicas ficam fora desta matriz.
            confusionMatrix = statuses.ToDictionary(expected => expected.ToString(), expected => statuses.ToDictionary(
                predicted => predicted.ToString(), predicted => rows.Count(row => row.Outcome == EvidenceOutcome.Completed && row.Expected == expected && row.Predicted == predicted))),
            results = rows
        };
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
        Summarize(output);
        if (report.technicalFailures > 0) Environment.ExitCode = 1;
    }

    // Diagnóstico offline da saída bruta: não transforma respostas rejeitadas em evidência aceita.
    public static void Summarize(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var rows = root["results"]!.AsArray();
        var statuses = Enum.GetNames<EvidenceStatus>();
        var matrix = new JsonObject();
        foreach (var expected in statuses)
        {
            var columns = new JsonObject();
            foreach (var predicted in statuses) columns[predicted] = 0;
            matrix[expected] = columns;
        }
        var correct = 0;
        var readable = 0;
        var validIds = 0;
        var falseSupported = new JsonArray();
        foreach (var row in rows)
        {
            string? modelStatus = null;
            try
            {
                using var output = JsonDocument.Parse(row!["rawResponse"]?.GetValue<string>() ?? "null");
                if (output.RootElement.ValueKind == JsonValueKind.Object
                    && output.RootElement.TryGetProperty("status", out var value)
                    && value.ValueKind == JsonValueKind.String && statuses.Contains(value.GetString()))
                    modelStatus = value.GetString();
            }
            catch (JsonException) { }
            row!["modelStatus"] = modelStatus;
            if (row["idsInRetrievedSet"]?.GetValue<bool>() == true) validIds++;
            if (modelStatus is null) continue;
            readable++;
            var expected = row["expected"]!.GetValue<string>();
            matrix[expected]![modelStatus] = matrix[expected]![modelStatus]!.GetValue<int>() + 1;
            if (modelStatus == expected) correct++;
            if (modelStatus == "SUPPORTED" && expected != "SUPPORTED") falseSupported.Add(row["id"]!.GetValue<string>());
        }
        root["modelReadableStatuses"] = readable;
        root["modelCorrect"] = correct;
        root["modelAccuracy"] = (double)correct / rows.Count;
        root["modelFalseSupported"] = falseSupported;
        root["modelConfusionMatrix"] = matrix;
        root["casesWithAllReturnedIdsValid"] = validIds;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Classificação bruta: {correct}/{rows.Count}; falsos SUPPORTED brutos: {falseSupported.Count}; respostas com IDs válidos: {validIds}/{rows.Count}; saídas aceitas corretas: {root["correct"]}/{rows.Count}.");
    }

    private static string[]? ExtractIds(string? text)
    {
        try
        {
            using var json = JsonDocument.Parse(text ?? "null");
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("evidenceIds", out var ids) || ids.ValueKind != JsonValueKind.Array
                || ids.EnumerateArray().Any(id => id.ValueKind != JsonValueKind.String)) return null;
            return ids.EnumerateArray().Select(id => id.GetString()!).ToArray();
        }
        catch (JsonException) { return null; }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed class RecordingChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public string? Payload { get; private set; }
        public string? ResponseText { get; private set; }
        public string? PromptSha256 { get; private set; }
        public float? Temperature { get; private set; }
        public int? MaxOutputTokens { get; private set; }
        public void Reset() { Payload = null; ResponseText = null; }

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var input = messages.ToArray();
            Payload = input.Single(message => message.Role == ChatRole.User).Text;
            PromptSha256 = Hash(input.Single(message => message.Role == ChatRole.System).Text);
            Temperature = options?.Temperature;
            MaxOutputTokens = options?.MaxOutputTokens;
            var response = await base.GetResponseAsync(input, options, cancellationToken);
            ResponseText = response.Text;
            return response;
        }
    }

    private sealed record RetrievedEvidence(string Id, string DocumentId, string Alias);
    private sealed record EvidenceRow(string Id, EvidenceStatus Expected, EvidenceOutcome Outcome, EvidenceStatus? Predicted,
        string? Justification, string? ClarificationQuestion, RetrievedEvidence[] RetrievedChunks, string[]? ReturnedIds,
        bool? IdsInRetrievedSet, string[] CitedDocuments, bool? ExpectedSourcesCited, string? RawResponse, string? Error,
        string[]? CanonicalEvidenceIds);
}
