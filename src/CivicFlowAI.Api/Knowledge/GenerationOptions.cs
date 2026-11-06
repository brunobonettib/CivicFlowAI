namespace CivicFlowAI.Api.Knowledge;

public sealed class GenerationOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "qwen2.5:1.5b";
    public int TimeoutSeconds { get; init; } = 120;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Generation:BaseUrl deve ser uma URL HTTP(S) sem credenciais.");
        if (string.IsNullOrWhiteSpace(Model)
            || Model.StartsWith("embeddinggemma", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Generation:Model deve indicar um modelo de geração, não embeddinggemma.");
        if (TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Use entre 1 e 3600 segundos.");
    }
}
