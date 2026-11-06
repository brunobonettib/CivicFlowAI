namespace CivicFlowAI.Api.Knowledge;

public sealed class SemanticSearchOptions
{
    public int TopK { get; init; } = 3;

    public void Validate() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TopK);
}

public sealed record SemanticSearchHit(KnowledgeChunk Chunk, double Score);

public enum SearchStatus { Completed, EmptyIndex, InvalidQuestion, InvalidEmbeddings, Unavailable, Cancelled }

public sealed record SemanticSearchResult(
    SearchStatus Status, IReadOnlyList<SemanticSearchHit> Results, string? Error = null);
