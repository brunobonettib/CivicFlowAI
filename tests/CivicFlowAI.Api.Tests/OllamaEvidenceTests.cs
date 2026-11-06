using System.Net;
using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using Xunit.Abstractions;

namespace CivicFlowAI.Api.Tests;

public sealed class OllamaEvidenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(200, EvidenceOutcome.Completed)]
    [InlineData(404, EvidenceOutcome.Unavailable)]
    public async Task AdapterSendsGenerationModelAndSchema(int statusCode, EvidenceOutcome expected)
    {
        using var handler = new ChatHandler(statusCode);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var chat = new OllamaApiClient(http, "qwen2.5:1.5b");
        using var generator = new StubEmbeddingGenerator();
        using var indexer = new EmbeddingIndexer(generator, EmbeddingIndexerTests.Chunks(1), new());
        await indexer.IndexAsync();
        var search = new SemanticSearchService(indexer, generator, new(), new());
        var service = new EvidenceAnalysisService(search, chat, new());
        var result = await service.AnalyzeAsync("Pergunta fora do domínio");
        Assert.Equal(expected, result.Outcome);
        if (expected == EvidenceOutcome.Completed) Assert.Equal(EvidenceStatus.UNSUPPORTED, result.Assessment!.Status);
        else Assert.Null(result.Assessment);
    }

    [GenerationIntegrationFact]
    [Trait("Category", "EvidenceIntegration")]
    public async Task EvaluatesFifteenRealCasesWithoutAssumingClassificationAccuracy()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddEnvironmentVariables().Build();
        var embeddingOptions = configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
        var generationOptions = configuration.GetSection("Generation").Get<GenerationOptions>()!;
        generationOptions.Validate();
        using var generationHttp = new HttpClient { BaseAddress = new Uri(generationOptions.BaseUrl), Timeout = TimeSpan.FromSeconds(generationOptions.TimeoutSeconds) };
        using var chat = new OllamaApiClient(generationHttp, generationOptions.Model);
        using var metadataTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var modelName = generationOptions.Model.Contains(':') ? generationOptions.Model : generationOptions.Model + ":latest";
        var installed = (await chat.ListLocalModelsAsync(metadataTimeout.Token)).Single(model => model.Name == modelName);
        output.WriteLine($"Geração: {installed.Name}; digest: {installed.Digest}");

        using var embeddingHttp = new HttpClient { BaseAddress = new Uri(embeddingOptions.BaseUrl), Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds) };
        using var generator = new OllamaApiClient(embeddingHttp, embeddingOptions.Model);
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var chunks = knowledge.Documents.SelectMany(new MarkdownChunker(configuration.GetSection("Chunking").Get<ChunkingOptions>()!).Split).ToArray();
        using var indexer = new EmbeddingIndexer(generator, chunks, embeddingOptions);
        Assert.Equal(IndexingStatus.Completed, (await indexer.IndexAsync()).Status);
        var search = new SemanticSearchService(indexer, generator, configuration.GetSection("SemanticSearch").Get<SemanticSearchOptions>()!, embeddingOptions);
        var service = new EvidenceAnalysisService(search, chat, generationOptions);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Evaluation", "questions.json")));
        var cases = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(15, cases.Length);
        var correct = 0;
        var failures = 0;
        foreach (var item in cases)
        {
            var expected = item.GetProperty("evidence").GetString() switch
            {
                "suficiente" => EvidenceStatus.SUPPORTED,
                "esclarecimento" => EvidenceStatus.NEEDS_CLARIFICATION,
                _ => EvidenceStatus.UNSUPPORTED
            };
            // Rótulos e notas nunca são enviados ao modelo.
            var result = await service.AnalyzeAsync(item.GetProperty("question").GetString());
            if (result.Outcome != EvidenceOutcome.Completed) failures++;
            else
            {
                if (result.Assessment!.Status == expected) correct++;
                Assert.All(result.Evidence!, chunk => Assert.Contains(chunk, chunks));
            }
            output.WriteLine($"{item.GetProperty("id").GetString()}: esperado={expected}; obtido={result.Assessment?.Status}; operação={result.Outcome}; IDs={string.Join(",", result.Assessment?.EvidenceIds ?? [])}");
        }
        output.WriteLine($"Concordância: {correct}/{cases.Length}; falhas técnicas: {failures}. Não é garantia de veracidade.");
        Assert.Equal(0, failures);
    }

    private sealed class ChatHandler(int statusCode) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("qwen2.5:1.5b", json.RootElement.GetProperty("model").GetString());
            Assert.Equal("object", json.RootElement.GetProperty("format").GetProperty("type").GetString());
            Assert.Equal(new[] { "F1" }, json.RootElement.GetProperty("format").GetProperty("properties")
                .GetProperty("evidenceIds").GetProperty("items").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal("system", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
            var content = JsonSerializer.Serialize(new
            {
                model = "qwen2.5:1.5b", done = true, done_reason = "stop",
                message = new { role = "assistant", content = """{"status":"UNSUPPORTED","evidenceIds":[],"justification":"Base sem cobertura.","clarificationQuestion":null}""" }
            });
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent(statusCode == 200 ? content : """{"error":"model not found"}""")
            };
        }
    }

    private sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new[] { 1f, 0f }))));
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

public sealed class GenerationIntegrationFactAttribute : FactAttribute
{
    public GenerationIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CIVICFLOW_EVIDENCE_INTEGRATION") != "1")
            Skip = "Requer modelo de geração instalado e CIVICFLOW_EVIDENCE_INTEGRATION=1; não baixa modelos.";
    }
}
