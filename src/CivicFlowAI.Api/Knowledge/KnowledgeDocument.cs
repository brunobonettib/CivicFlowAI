namespace CivicFlowAI.Api.Knowledge;

public sealed record KnowledgeMetadata(
    string Id,
    string Titulo,
    string Orgao,
    Uri Url,
    DateOnly DataConsulta,
    DateOnly? DataAtualizacao,
    Uri Licenca);

public sealed record KnowledgeDocument(KnowledgeMetadata Metadata, string Conteudo);
