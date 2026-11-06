using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;

namespace CivicFlowAI.Api.Tests;

public sealed class EmbeddingIndexerTests
{
    [Fact]
    public async Task GeneratesSmallBatchesAndPreservesEveryChunkAndVector()
    {
        using var generator = new FakeGenerator();
        var chunks = Chunks(5);
        using var indexer = new EmbeddingIndexer(generator, chunks, new());
        Assert.Empty(generator.Calls);
        Assert.Empty(indexer.Current.Items);

        var result = await indexer.IndexAsync();

        Assert.Equal(IndexingStatus.Completed, result.Status);
        Assert.Equal(new[] { 2, 2, 1 }, generator.Calls.Select(c => c.Length));
        Assert.Equal(chunks.Select(c => c.Conteudo), generator.Calls.SelectMany(c => c));
        Assert.Equal("embeddinggemma", indexer.Current.Model);
        Assert.Equal(2, indexer.Current.Dimensions);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i], indexer.Current.Items[i].Chunk);
            Assert.Equal(new[] { (float)i, 1f }, indexer.Current.Items[i].Vector);
        }
        Assert.Equal("embeddinggemma", generator.LastOptions?.ModelId);
        Assert.Equal(false, generator.LastOptions?.AdditionalProperties?["truncate"]);
    }

    [Fact]
    public async Task CopiesProviderVectorsAndReplacesInsteadOfAppending()
    {
        var vector = new[] { 1f, 2f };
        using var generator = new FakeGenerator { Handler = (_, _, _) => Task.FromResult(Vectors(vector)) };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        await indexer.IndexAsync();
        var old = indexer.Current;
        vector[0] = 9;
        Assert.Equal(1f, old.Items[0].Vector[0]);
        await indexer.IndexAsync();
        Assert.Single(indexer.Current.Items);
        Assert.Equal(9f, indexer.Current.Items[0].Vector[0]);
        Assert.Equal(1f, old.Items[0].Vector[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RejectsWrongVectorCount(int count)
    {
        using var generator = new FakeGenerator
        {
            Handler = (_, _, _) => Task.FromResult(Vectors(Enumerable.Range(0, count).Select(_ => new[] { 1f }).ToArray()))
        };
        using var indexer = new EmbeddingIndexer(generator, Chunks(2), new());
        Assert.Equal(IndexingStatus.InvalidEmbeddings, (await indexer.IndexAsync()).Status);
        Assert.Empty(indexer.Current.Items);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task RejectsNonFiniteValues(float value)
    {
        using var generator = new FakeGenerator { Handler = (_, _, _) => Task.FromResult(Vectors([1f, value])) };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        Assert.Equal(IndexingStatus.InvalidEmbeddings, (await indexer.IndexAsync()).Status);
        Assert.Empty(indexer.Current.Items);
    }

    [Fact]
    public async Task RejectsEmptyVector()
    {
        using var generator = new FakeGenerator { Handler = (_, _, _) => Task.FromResult(Vectors(Array.Empty<float>())) };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        Assert.Equal(IndexingStatus.InvalidEmbeddings, (await indexer.IndexAsync()).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RejectsInconsistentDimensionsWithinAndAcrossBatches(int batchSize)
    {
        using var generator = new FakeGenerator
        {
            Handler = (texts, _, _) => Task.FromResult(Vectors(texts.Select(t =>
                t == "0" ? new[] { 1f } : new[] { 1f, 2f }).ToArray()))
        };
        using var indexer = new EmbeddingIndexer(generator, Chunks(2), new() { BatchSize = batchSize });
        Assert.Equal(IndexingStatus.InvalidEmbeddings, (await indexer.IndexAsync()).Status);
        Assert.Empty(indexer.Current.Items);
    }

    [Theory]
    [InlineData("network", IndexingStatus.Unavailable)]
    [InlineData("provider", IndexingStatus.Unavailable)]
    [InlineData("model", IndexingStatus.Unavailable)]
    [InlineData("timeout", IndexingStatus.Unavailable)]
    [InlineData("json", IndexingStatus.InvalidEmbeddings)]
    public async Task HandlesExpectedProviderFailuresWithoutExposingPayload(string failure, IndexingStatus status)
    {
        using var generator = new FakeGenerator
        {
            Handler = (_, _, _) => throw failure switch
            {
                "network" => new HttpRequestException("sensitive input"),
                "provider" => new OllamaException("sensitive input"),
                "model" => new ResponseError("sensitive input"),
                "timeout" => new TaskCanceledException("sensitive input"),
                _ => new JsonException("sensitive input")
            }
        };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        var result = await indexer.IndexAsync();
        Assert.Equal(status, result.Status);
        Assert.DoesNotContain("sensitive", result.Error!);
        Assert.Empty(indexer.Current.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReindexKeepsPreviousCompleteSnapshot(bool invalidVector)
    {
        using var generator = new FakeGenerator();
        using var indexer = new EmbeddingIndexer(generator, Chunks(3), new());
        await indexer.IndexAsync();
        var previous = indexer.Current;
        generator.Handler = (texts, _, _) =>
        {
            Assert.Same(previous, indexer.Current);
            if (texts[0] == "2")
            {
                if (invalidVector) return Task.FromResult(Vectors([float.NaN, 1f]));
                throw new HttpRequestException();
            }
            return Task.FromResult(Vectors(texts.Select(_ => new[] { 9f, 9f }).ToArray()));
        };
        var result = await indexer.IndexAsync();
        Assert.Equal(invalidVector ? IndexingStatus.InvalidEmbeddings : IndexingStatus.Unavailable, result.Status);
        Assert.Same(previous, indexer.Current);
        generator.Handler = null;
        Assert.Equal(IndexingStatus.Completed, (await indexer.IndexAsync()).Status);
    }

    [Fact]
    public async Task CancellationBeforeIndexMakesNoProviderCall()
    {
        using var generator = new FakeGenerator();
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(IndexingStatus.Cancelled, (await indexer.IndexAsync(cancellation.Token)).Status);
        Assert.Empty(generator.Calls);
    }

    [Fact]
    public async Task CancellationDuringReindexKeepsPreviousSnapshotEvenIfProviderReturns()
    {
        using var generator = new FakeGenerator();
        using var indexer = new EmbeddingIndexer(generator, Chunks(3), new());
        await indexer.IndexAsync();
        var previous = indexer.Current;
        using var cancellation = new CancellationTokenSource();
        generator.Handler = (texts, _, token) =>
        {
            Assert.True(token.CanBeCanceled);
            if (texts[0] == "2") cancellation.Cancel();
            return Task.FromResult(Vectors(texts.Select(_ => new[] { 9f, 9f }).ToArray()));
        };
        Assert.Equal(IndexingStatus.Cancelled, (await indexer.IndexAsync(cancellation.Token)).Status);
        Assert.Same(previous, indexer.Current);
    }

    [Fact]
    public async Task CancelsQueuedCallWithoutStartingAnotherGeneration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var generator = new FakeGenerator
        {
            Handler = async (_, _, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return Vectors([1f, 2f]);
            }
        };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new());
        var first = indexer.IndexAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = indexer.IndexAsync(cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(IndexingStatus.Cancelled, (await second).Status);
        Assert.Single(generator.Calls);
        Assert.Empty(indexer.Current.Items);
        release.SetResult();
        Assert.Equal(IndexingStatus.Completed, (await first).Status);
    }

    [Fact]
    public async Task EnforcesConfiguredOperationTimeout()
    {
        using var generator = new FakeGenerator
        {
            Handler = async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Vectors([1f]);
            }
        };
        using var indexer = new EmbeddingIndexer(generator, Chunks(1), new() { TimeoutSeconds = 1 });
        Assert.Equal(IndexingStatus.Unavailable, (await indexer.IndexAsync()).Status);
        Assert.Empty(indexer.Current.Items);
    }

    [Fact]
    public async Task EmptyIndexRequiresNoProviderCall()
    {
        using var generator = new FakeGenerator();
        using var indexer = new EmbeddingIndexer(generator, [], new());
        Assert.Equal(IndexingStatus.Completed, (await indexer.IndexAsync()).Status);
        Assert.Empty(generator.Calls);
        Assert.Equal(0, indexer.Current.Dimensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsDuplicateIdsOrEmptyContentBeforeCallingProvider(bool duplicate)
    {
        var chunks = Chunks(2).ToArray();
        chunks[1] = duplicate ? chunks[0] : chunks[1] with { Conteudo = " " };
        using var generator = new FakeGenerator();
        using var indexer = new EmbeddingIndexer(generator, chunks, new());
        Assert.Equal(IndexingStatus.InvalidEmbeddings, (await indexer.IndexAsync()).Status);
        Assert.Empty(generator.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void RejectsInvalidBatchSize(int batchSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaEmbeddingOptions { BatchSize = batchSize }.Validate());

    internal static IReadOnlyList<KnowledgeChunk> Chunks(int count) => Enumerable.Range(0, count)
        .Select(i => new KnowledgeChunk($"chunk-{i}", $"doc-{i}", $"Título {i}",
            new Uri($"https://www.gov.br/doc-{i}"), $"Seção {i}", i.ToString())).ToArray();

    private static GeneratedEmbeddings<Embedding<float>> Vectors(params float[][] vectors) =>
        new(vectors.Select(vector => new Embedding<float>(vector)));

    private sealed class FakeGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string[]> Calls { get; } = [];
        public EmbeddingGenerationOptions? LastOptions { get; private set; }
        public Func<string[], EmbeddingGenerationOptions?, CancellationToken, Task<GeneratedEmbeddings<Embedding<float>>>>? Handler { get; set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var texts = values.ToArray();
            Calls.Add(texts);
            LastOptions = options;
            return Handler?.Invoke(texts, options, cancellationToken)
                ?? Task.FromResult(Vectors(texts.Select(t => new[] { float.Parse(t), 1f }).ToArray()));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
