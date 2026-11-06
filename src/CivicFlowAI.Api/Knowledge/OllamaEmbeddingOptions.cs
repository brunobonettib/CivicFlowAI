namespace CivicFlowAI.Api.Knowledge;

public sealed class OllamaEmbeddingOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "embeddinggemma";
    public int BatchSize { get; init; } = 2;
    public int TimeoutSeconds { get; init; } = 120;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Ollama:BaseUrl deve ser uma URL HTTP(S) sem credenciais.");
        if (string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("Ollama:Model é obrigatório.");
        if (BatchSize is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "Use lotes de 1 a 16 chunks.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TimeoutSeconds);
    }
}
