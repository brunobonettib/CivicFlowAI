using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using Xunit.Abstractions;

namespace CivicFlowAI.Api.Tests;

public sealed class OllamaSemanticSearchTests(ITestOutputHelper output)
{
    [OllamaIntegrationFact]
    [Trait("Category", "OllamaIntegration")]
    public async Task SearchesRealDocumentsWithLocalEmbeddings()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddEnvironmentVariables().Build();
        var options = configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>()!;
        using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        using var generator = new OllamaApiClient(http, options.Model);
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var chunker = new MarkdownChunker(configuration.GetSection("Chunking").Get<ChunkingOptions>()!);
        var chunks = knowledge.Documents.SelectMany(chunker.Split).ToArray();
        using var indexer = new EmbeddingIndexer(generator, chunks, options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var indexing = await indexer.IndexAsync(cancellation.Token);
        Assert.True(indexing.Status == IndexingStatus.Completed, indexing.Error);
        var snapshot = indexer.Current;
        var search = new SemanticSearchService(indexer, generator, new() { TopK = 3 }, options);

        foreach (var question in new[]
        {
            "Como registrar um pedido de acesso à informação?",
            "Qual o prazo de resposta da LAI e como funciona a prorrogação?",
            "Como acompanhar uma solicitação de informação?"
        })
        {
            var result = await search.SearchAsync(question, cancellation.Token);
            Assert.True(result.Status == SearchStatus.Completed, result.Error);
            Assert.Equal(Math.Min(3, chunks.Length), result.Results.Count);
            Assert.Equal(result.Results.OrderByDescending(hit => hit.Score), result.Results);
            Assert.All(result.Results, hit =>
            {
                Assert.Contains(hit.Chunk, chunks);
                Assert.True(double.IsFinite(hit.Score));
                Assert.InRange(hit.Score, -1d, 1d);
            });
            Assert.Same(snapshot, indexer.Current);
            output.WriteLine($"{question} => {string.Join(", ", result.Results.Select(hit => $"{hit.Chunk.DocumentoId}={hit.Score:F4}"))}");
        }

        // Recuperar o próprio conteúdo verifica a associação sem impor limiar de relevância.
        var reference = chunks.Single(chunk => chunk.DocumentoId == "lai-prazos-mdic");
        var exact = await search.SearchAsync(reference.Conteudo, cancellation.Token);
        Assert.Equal(SearchStatus.Completed, exact.Status);
        Assert.Equal(reference.Id, exact.Results[0].Chunk.Id);
        output.WriteLine($"Índice: {snapshot.Items.Count} chunks, {snapshot.Dimensions} dimensões; associação preservada.");
    }
}
