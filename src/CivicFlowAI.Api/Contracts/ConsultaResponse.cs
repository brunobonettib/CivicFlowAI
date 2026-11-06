namespace CivicFlowAI.Api.Contracts;

public sealed record ConsultaResponse(
    string Status,
    string Resposta,
    IReadOnlyList<FonteResponse> Fontes,
    IReadOnlyList<TrechoResponse> Trechos);

public sealed record FonteResponse(
    string Id,
    string Titulo,
    Uri Url,
    DateOnly DataConsulta);

public sealed record TrechoResponse(string FonteId, string Texto);
