namespace CivicFlowAI.Api.Knowledge;

public sealed record KnowledgeChunk(
    string Id,
    string DocumentoId,
    string Titulo,
    Uri Url,
    string Secao,
    string Conteudo);

public sealed class ChunkingOptions
{
    public int TargetCharacters { get; init; } = 1000;
}
