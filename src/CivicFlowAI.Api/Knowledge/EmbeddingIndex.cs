namespace CivicFlowAI.Api.Knowledge;

public sealed record EmbeddedChunk(KnowledgeChunk Chunk, IReadOnlyList<float> Vector);

public sealed record EmbeddingIndex(string? Model, int Dimensions, IReadOnlyList<EmbeddedChunk> Items);

public enum IndexingStatus { Completed, Unavailable, Cancelled, InvalidEmbeddings }

public sealed record IndexingResult(IndexingStatus Status, string? Error = null);
