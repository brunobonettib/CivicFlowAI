using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;

namespace CivicFlowAI.Api.Tests;

public sealed class RagServiceTests
{
    [Fact]
    public async Task AnswersWithExactEvidenceAndCatalogSourcesWithoutReindexing()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.ConsultAsync("Como consultar informações?");
        Assert.Equal(200, result.StatusCode);
        var answer = result.Response!;
        Assert.Equal("SUPPORTED", answer.Status);
        var source = Assert.Single(answer.Fontes);
        var metadata = fixture.Knowledge.Documents.Single(d => d.Metadata.Id == source.Id).Metadata;
        Assert.Equal(metadata.Url, source.Url);
        Assert.Equal(metadata.Titulo, source.Titulo);
        Assert.Equal(metadata.DataConsulta, source.DataConsulta);
        var excerpt = Assert.Single(answer.Trechos);
        Assert.Equal(source.Id, excerpt.FonteId);
        Assert.Contains(excerpt.Texto, fixture.Chunks.Single(c => c.DocumentoId == source.Id).Conteudo);
        Assert.Equal($"{excerpt.Texto} [{source.Id}]", answer.Resposta);
        var calls = fixture.Generator.Calls;
        await fixture.Service.ConsultAsync("Outra pergunta");
        Assert.Equal(calls + 1, fixture.Generator.Calls);
    }

    [Theory]
    [InlineData("UNSUPPORTED")]
    [InlineData("NEEDS_CLARIFICATION")]
    public async Task HandlesEvidenceDecisionsWithoutGeneratingAnAnswer(string status)
    {
        using var fixture = new Fixture();
        fixture.Chat.Assessment = JsonSerializer.Serialize(new
        {
            status, evidenceIds = Array.Empty<string>(), justification = "Contexto insuficiente.",
            clarificationQuestion = status == "NEEDS_CLARIFICATION" ? "A qual serviço você se refere?" : null
        });
        var result = await fixture.Service.ConsultAsync("Pergunta");
        Assert.Equal(status, result.Response!.Status);
        Assert.Empty(result.Response.Fontes);
        Assert.Empty(result.Response.Trechos);
        Assert.Equal(0, fixture.Chat.AnswerCalls);
    }

    [Fact]
    public async Task FinalCoverageCheckCanRejectFalseSupported()
    {
        using var fixture = new Fixture();
        fixture.Chat.Answer = """{"contextIdentified":true,"missingInformation":[],"status":"UNSUPPORTED","passageIds":[]}""";
        var result = await fixture.Service.ConsultAsync("Pergunta sem cobertura");
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("UNSUPPORTED", result.Response!.Status);
        Assert.Empty(result.Response.Fontes);
        Assert.Empty(result.Response.Trechos);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":[]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":["P999"]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":["P1","P1"]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":[1]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":["p1"]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":[" P1"]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"UNSUPPORTED","passageIds":["P1"]}""")]
    [InlineData("""{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":["P1"],"url":"https://fake.example"}""")]
    [InlineData("""{"status":"SUPPORTED","status":"SUPPORTED","passageIds":["P1"]}""")]
    public async Task RejectsInvalidFinalResponses(string response)
    {
        using var fixture = new Fixture();
        fixture.Chat.Answer = response;
        var result = await fixture.Service.ConsultAsync("Pergunta");
        Assert.Equal(502, result.StatusCode);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task RejectsSourceMetadataMismatch()
    {
        using var fixture = new Fixture(tamperUrl: true);
        var result = await fixture.Service.ConsultAsync("Pergunta");
        Assert.Equal(502, result.StatusCode);
        Assert.Equal(0, fixture.Chat.AnswerCalls);
    }

    [Theory]
    [InlineData(false, "[]", "NEEDS_CLARIFICATION")]
    [InlineData(true, "[\"Condições não descritas\"]", "UNSUPPORTED")]
    public async Task ExplicitCoverageGapsOverrideSupported(bool context, string missing, string expected)
    {
        using var fixture = new Fixture();
        fixture.Chat.Answer = $$"""{"contextIdentified":{{context.ToString().ToLowerInvariant()}},"missingInformation":{{missing}},"status":"SUPPORTED","passageIds":["P1"]}""";
        var result = await fixture.Service.ConsultAsync("Pergunta");
        Assert.Equal(expected, result.Response!.Status);
        Assert.Empty(result.Response.Fontes);
    }

    [Fact]
    public async Task ReturnsServiceUnavailableForOllamaFailures()
    {
        using var fixture = new Fixture();
        fixture.Chat.Fail = true;
        Assert.Equal(503, (await fixture.Service.ConsultAsync("Pergunta")).StatusCode);
    }

    [Fact]
    public async Task ReturnsBadGatewayForInvalidEvidenceOutput()
    {
        using var fixture = new Fixture();
        fixture.Chat.Assessment = "{}";
        Assert.Equal(502, (await fixture.Service.ConsultAsync("Pergunta")).StatusCode);
        Assert.Equal(0, fixture.Chat.AnswerCalls);
    }

    [Fact]
    public async Task HandlesCancellationAndDeadline()
    {
        using var fixture = new Fixture(timeout: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(499, (await fixture.Service.ConsultAsync("Pergunta", cancellation.Token)).StatusCode);
        fixture.Chat.Hang = true;
        Assert.Equal(504, (await fixture.Service.ConsultAsync("Pergunta")).StatusCode);
    }

    [Fact]
    public async Task DoesNotPublishTruncatedAnswer()
    {
        using var fixture = new Fixture();
        fixture.Chat.Truncated = true;
        Assert.Equal(502, (await fixture.Service.ConsultAsync("Pergunta")).StatusCode);
    }

    private sealed class Fixture : IDisposable
    {
        public KnowledgeBase Knowledge { get; } = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        public KnowledgeChunk[] Chunks { get; }
        public FakeGenerator Generator { get; } = new();
        public FakeChat Chat { get; } = new();
        public EmbeddingIndexer Indexer { get; }
        public RagService Service { get; }

        public Fixture(bool tamperUrl = false, int timeout = 120)
        {
            Chunks = Knowledge.Documents.SelectMany(new MarkdownChunker(new()).Split).ToArray();
            if (tamperUrl)
                for (var i = 0; i < Chunks.Length; i++) Chunks[i] = Chunks[i] with { Url = new Uri("https://fake.example") };
            Indexer = new(Generator, Chunks, new());
            var search = new SemanticSearchService(Indexer, Generator, new(), new());
            var options = new GenerationOptions { TimeoutSeconds = timeout };
            Service = new(Indexer, new(search, Chat, options), Chat, Knowledge, Chunks, options);
        }
        public void Dispose() { Service.Dispose(); Indexer.Dispose(); Chat.Dispose(); Generator.Dispose(); }
    }

    private sealed class FakeGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls { get; private set; }
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new[] { 1f, 0f }))));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeChat : IChatClient
    {
        public string Assessment { get; set; } = """{"status":"SUPPORTED","evidenceIds":["F1"],"justification":"Há cobertura.","clarificationQuestion":null}""";
        public string Answer { get; set; } = """{"contextIdentified":true,"missingInformation":[],"status":"SUPPORTED","passageIds":["P1"]}""";
        public int AnswerCalls { get; private set; }
        public bool Fail { get; set; }
        public bool Hang { get; set; }
        public bool Truncated { get; set; }
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new HttpRequestException();
            if (Hang) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var final = messages.First().Text.Contains("EXTRATIVA", StringComparison.Ordinal);
            if (final) AnswerCalls++;
            return new(new ChatMessage(ChatRole.Assistant, final ? Answer : Assessment))
            {
                FinishReason = final && Truncated ? ChatFinishReason.Length : ChatFinishReason.Stop
            };
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
