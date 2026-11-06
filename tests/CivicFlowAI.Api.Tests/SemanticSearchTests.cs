using System.Text.Json;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;

namespace CivicFlowAI.Api.Tests;

public sealed class SemanticSearchTests
{
    [Fact]
    public async Task RanksCosinesAndPreservesSourcesWithoutReindexing()
    {
        using var fixture = new Fixture([[0f, 1f], [-1f, 0f], [2f, 0f], [1f, 1f]], topK: 4);
        await fixture.IndexAsync();
        var snapshot = fixture.Indexer.Current;
        var calls = fixture.Generator.Calls.Count;
        var result = await fixture.Search.SearchAsync("  pergunta  ");
        Assert.Equal(SearchStatus.Completed, result.Status);
        Assert.Equal(new[] { "chunk-2", "chunk-3", "chunk-0", "chunk-1" }, result.Results.Select(hit => hit.Chunk.Id));
        Assert.Equal(1d, result.Results[0].Score, 12);
        Assert.Equal(1 / Math.Sqrt(2), result.Results[1].Score, 12);
        Assert.Equal(0d, result.Results[2].Score, 12);
        Assert.Equal(-1d, result.Results[3].Score, 12);
        Assert.All(result.Results, hit => Assert.Equal(snapshot.Items.Single(item => item.Chunk.Id == hit.Chunk.Id).Chunk, hit.Chunk));
        Assert.Equal(new[] { "pergunta" }, fixture.Generator.Calls.Last());
        Assert.Equal(snapshot.Model, fixture.Generator.LastOptions!.ModelId);
        Assert.Equal(false, fixture.Generator.LastOptions.AdditionalProperties!["truncate"]);
        await fixture.Search.SearchAsync("segunda pergunta");
        Assert.Equal(calls + 2, fixture.Generator.Calls.Count);
        Assert.Same(snapshot, fixture.Indexer.Current);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(10, 4)]
    public async Task AppliesTopKWithDeterministicTies(int topK, int expected)
    {
        using var fixture = new Fixture([[1f, 0f], [1f, 0f], [1f, 0f], [1f, 0f]], topK);
        await fixture.IndexAsync();
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(expected, result.Results.Count);
        Assert.Equal(Enumerable.Range(0, expected).Select(i => $"chunk-{i}"), result.Results.Select(hit => hit.Chunk.Id));
    }

    [Fact]
    public async Task EmptyIndexDoesNotCallGeneratorOrTriggerIndexing()
    {
        using var fixture = new Fixture([[1f, 0f]]);
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(SearchStatus.EmptyIndex, result.Status);
        Assert.Empty(result.Results);
        Assert.Empty(fixture.Generator.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task RejectsInvalidQuestion(string? question)
    {
        using var fixture = new Fixture([]);
        Assert.Equal(SearchStatus.InvalidQuestion, (await fixture.Search.SearchAsync(question)).Status);
        Assert.Empty(fixture.Generator.Calls);
    }

    [Fact]
    public async Task RejectsOversizedQuestion()
    {
        using var fixture = new Fixture([]);
        Assert.Equal(SearchStatus.InvalidQuestion, (await fixture.Search.SearchAsync(new string('a', 2001))).Status);
        Assert.Empty(fixture.Generator.Calls);
    }

    public static IEnumerable<object[]> InvalidQueryVectors()
    {
        yield return [Array.Empty<float>()];
        yield return [new[] { 1f }];
        yield return [new[] { 1f, 2f, 3f }];
        yield return [new[] { 0f, 0f }];
        yield return [new[] { float.NaN, 1f }];
        yield return [new[] { float.PositiveInfinity, 1f }];
        yield return [new[] { 1f, float.NegativeInfinity }];
    }

    [Theory]
    [MemberData(nameof(InvalidQueryVectors))]
    public async Task RejectsInvalidOrIncompatibleQueryVectors(float[] query)
    {
        using var fixture = new Fixture([[1f, 0f]]);
        await fixture.IndexAsync();
        fixture.Generator.Reply = (_, _, _) => Task.FromResult(Vectors(query));
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(SearchStatus.InvalidEmbeddings, result.Status);
        Assert.Empty(result.Results);
        Assert.Single(fixture.Indexer.Current.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RejectsWrongQueryEmbeddingCount(int count)
    {
        using var fixture = new Fixture([[1f, 0f]]);
        await fixture.IndexAsync();
        fixture.Generator.Reply = (_, _, _) => Task.FromResult(Vectors(Enumerable.Range(0, count).Select(_ => new[] { 1f, 0f }).ToArray()));
        Assert.Equal(SearchStatus.InvalidEmbeddings, (await fixture.Search.SearchAsync("pergunta")).Status);
    }

    [Fact]
    public async Task RejectsZeroNormDocumentWithoutReturningPartialResults()
    {
        using var fixture = new Fixture([[1f, 0f], [0f, 0f]]);
        await fixture.IndexAsync();
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(SearchStatus.InvalidEmbeddings, result.Status);
        Assert.Empty(result.Results);
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.Epsilon)]
    public async Task AvoidsFloatOverflowAndUnderflow(float magnitude)
    {
        using var fixture = new Fixture([[magnitude, magnitude]]);
        await fixture.IndexAsync();
        fixture.Generator.Reply = (_, _, _) => Task.FromResult(Vectors([magnitude, magnitude]));
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(SearchStatus.Completed, result.Status);
        Assert.Equal(1d, Assert.Single(result.Results).Score, 12);
    }

    [Theory]
    [InlineData("http", SearchStatus.Unavailable)]
    [InlineData("ollama", SearchStatus.Unavailable)]
    [InlineData("timeout", SearchStatus.Unavailable)]
    [InlineData("json", SearchStatus.InvalidEmbeddings)]
    public async Task HandlesProviderFailures(string error, SearchStatus expected)
    {
        using var fixture = new Fixture([[1f, 0f]]);
        await fixture.IndexAsync();
        var snapshot = fixture.Indexer.Current;
        fixture.Generator.Reply = (_, _, _) => throw error switch
        {
            "http" => new HttpRequestException("sensitive input"),
            "ollama" => new OllamaException("sensitive input"),
            "timeout" => new TaskCanceledException("sensitive input"),
            _ => new JsonException("sensitive input")
        };
        var result = await fixture.Search.SearchAsync("pergunta");
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Results);
        Assert.DoesNotContain("sensitive", result.Error!);
        Assert.Same(snapshot, fixture.Indexer.Current);
    }

    [Fact]
    public async Task HandlesCancellationBeforeAndDuringGeneration()
    {
        using var fixture = new Fixture([[1f, 0f]]);
        await fixture.IndexAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Generator.Reply = (_, _, token) =>
        {
            cancellation.Cancel();
            Assert.True(token.IsCancellationRequested);
            return Task.FromResult(Vectors([1f, 0f]));
        };
        var result = await fixture.Search.SearchAsync("pergunta", cancellation.Token);
        Assert.Equal(SearchStatus.Cancelled, result.Status);
        Assert.Empty(result.Results);
        var calls = fixture.Generator.Calls.Count;
        Assert.Equal(SearchStatus.Cancelled, (await fixture.Search.SearchAsync("pergunta", cancellation.Token)).Status);
        Assert.Equal(calls, fixture.Generator.Calls.Count);
    }

    [Fact]
    public async Task EnforcesQueryTimeout()
    {
        using var fixture = new Fixture([[1f, 0f]], timeoutSeconds: 1);
        await fixture.IndexAsync();
        fixture.Generator.Reply = async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Vectors([1f, 0f]);
        };
        Assert.Equal(SearchStatus.Unavailable, (await fixture.Search.SearchAsync("pergunta")).Status);
    }

    [Fact]
    public async Task UsesCapturedIndexAndItsModelWhileReindexing()
    {
        using var fixture = new Fixture([[1f, 0f]]);
        await fixture.IndexAsync();
        var previous = fixture.Indexer.Current;
        using var queryGenerator = new FakeGenerator();
        queryGenerator.Reply = async (_, options, _) =>
        {
            Assert.Equal(previous.Model, options!.ModelId);
            fixture.Generator.Reply = (_, _, _) => Task.FromResult(Vectors([0f, 1f]));
            await fixture.IndexAsync();
            return Vectors([1f, 0f]);
        };
        var service = new SemanticSearchService(fixture.Indexer, queryGenerator, new(), new() { Model = "other-default" });
        var result = await service.SearchAsync("pergunta");
        Assert.Equal(1d, Assert.Single(result.Results).Score, 12);
        Assert.NotSame(previous, fixture.Indexer.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidTopK(int topK) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticSearchOptions { TopK = topK }.Validate());

    private static GeneratedEmbeddings<Embedding<float>> Vectors(params float[][] vectors) =>
        new(vectors.Select(vector => new Embedding<float>(vector)));

    private sealed class Fixture : IDisposable
    {
        public FakeGenerator Generator { get; } = new();
        public EmbeddingIndexer Indexer { get; }
        public SemanticSearchService Search { get; }

        public Fixture(float[][] vectors, int topK = 3, int timeoutSeconds = 120)
        {
            var options = new OllamaEmbeddingOptions { TimeoutSeconds = timeoutSeconds };
            Generator.Reply = (texts, _, _) => Task.FromResult(Vectors(texts.Select(t => vectors[int.Parse(t)]).ToArray()));
            Indexer = new(Generator, EmbeddingIndexerTests.Chunks(vectors.Length), options);
            Search = new(Indexer, Generator, new() { TopK = topK }, options);
        }

        public async Task IndexAsync()
        {
            Assert.Equal(IndexingStatus.Completed, (await Indexer.IndexAsync()).Status);
            Generator.Reply = (_, _, _) => Task.FromResult(Vectors([1f, 0f]));
        }

        public void Dispose() { Indexer.Dispose(); Generator.Dispose(); }
    }

    private sealed class FakeGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string[]> Calls { get; } = [];
        public EmbeddingGenerationOptions? LastOptions { get; private set; }
        public Func<string[], EmbeddingGenerationOptions?, CancellationToken, Task<GeneratedEmbeddings<Embedding<float>>>> Reply { get; set; } = null!;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var texts = values.ToArray();
            Calls.Add(texts);
            LastOptions = options;
            return Reply(texts, options, cancellationToken);
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
