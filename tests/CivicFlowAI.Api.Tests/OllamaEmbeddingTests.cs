using System.Net;
using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

namespace CivicFlowAI.Api.Tests;

public sealed class OllamaEmbeddingTests
{
    [Fact]
    public async Task AdapterUsesEmbedEndpointModelOrderAndNoTruncation()
    {
        using var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("embeddinggemma", json.RootElement.GetProperty("model").GetString());
            Assert.False(json.RootElement.GetProperty("truncate").GetBoolean());
            Assert.Equal(new[] { "0", "1" }, json.RootElement.GetProperty("input").EnumerateArray().Select(v => v.GetString()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"model":"embeddinggemma","embeddings":[[1,2],[3,4]]}""")
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var generator = new OllamaApiClient(http, "embeddinggemma");
        using var indexer = new EmbeddingIndexer(generator, EmbeddingIndexerTests.Chunks(2), new());
        Assert.Equal(IndexingStatus.Completed, (await indexer.IndexAsync()).Status);
        Assert.Equal(new[] { 1f, 2f }, indexer.Current.Items[0].Vector);
        Assert.Equal(new[] { 3f, 4f }, indexer.Current.Items[1].Vector);
    }

    [Theory]
    [InlineData(404, "{\"error\":\"model missing\"}", IndexingStatus.Unavailable)]
    [InlineData(500, "{\"error\":\"server error\"}", IndexingStatus.Unavailable)]
    [InlineData(200, "{", IndexingStatus.InvalidEmbeddings)]
    public async Task AdapterFailuresAreHandled(int status, string body, IndexingStatus expected)
    {
        using var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body)
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var generator = new OllamaApiClient(http, "embeddinggemma");
        using var indexer = new EmbeddingIndexer(generator, EmbeddingIndexerTests.Chunks(1), new());
        Assert.Equal(expected, (await indexer.IndexAsync()).Status);
        Assert.Empty(indexer.Current.Items);
    }

    [OllamaIntegrationFact]
    [Trait("Category", "OllamaIntegration")]
    public async Task IndexesRealKnowledgeWithLocalOllama()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddEnvironmentVariables().Build();
        var options = configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
        options.Validate();
        using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        using var generator = new OllamaApiClient(http, options.Model);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var version = await generator.GetVersionAsync(timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(version));
        var models = await generator.ListLocalModelsAsync(timeout.Token);
        var modelName = options.Model.Contains(':') ? options.Model : options.Model + ":latest";
        Assert.Contains(models, model => model.Name == modelName);

        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var chunker = new MarkdownChunker(configuration.GetSection("Chunking").Get<ChunkingOptions>()!);
        var chunks = knowledge.Documents.SelectMany(chunker.Split).ToArray();
        using var indexer = new EmbeddingIndexer(generator, chunks, options);
        var result = await indexer.IndexAsync(timeout.Token);
        Assert.True(result.Status == IndexingStatus.Completed, result.Error);
        Assert.Equal(chunks.Length, indexer.Current.Items.Count);
        Assert.Equal(3, indexer.Current.Items.Select(item => item.Chunk.DocumentoId).Distinct().Count());
        Assert.True(indexer.Current.Dimensions > 0);
        Assert.Equal(chunks, indexer.Current.Items.Select(item => item.Chunk));
        Assert.All(indexer.Current.Items, item =>
        {
            Assert.Equal(indexer.Current.Dimensions, item.Vector.Count);
            Assert.All(item.Vector, value => Assert.True(float.IsFinite(value)));
        });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}

public sealed class OllamaIntegrationFactAttribute : FactAttribute
{
    public OllamaIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CIVICFLOW_OLLAMA_INTEGRATION") != "1")
            Skip = "Integração real opcional: requer Ollama local, modelo instalado e CIVICFLOW_OLLAMA_INTEGRATION=1.";
    }
}
