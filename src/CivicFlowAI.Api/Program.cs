using CivicFlowAI.Api.Contracts;
using CivicFlowAI.Api.Knowledge;
using Microsoft.Extensions.AI;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args); 
builder.Services.AddProblemDetails();
var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
var chunker = new MarkdownChunker(builder.Configuration.GetSection("Chunking").Get<ChunkingOptions>() ?? new());
builder.Services.AddSingleton(knowledge); 
builder.Services.AddSingleton(chunker);
builder.Services.AddSingleton<IReadOnlyList<KnowledgeChunk>>(
    knowledge.Documents.SelectMany(chunker.Split).ToList().AsReadOnly());

var ollamaOptions = builder.Configuration.GetSection("Ollama").Get<OllamaEmbeddingOptions>() ?? new();
ollamaOptions.Validate();
builder.Services.AddSingleton(ollamaOptions);
builder.Services.AddHttpClient("OllamaEmbeddings", client =>
{
    client.BaseAddress = new Uri(ollamaOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(ollamaOptions.TimeoutSeconds);
});
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(services =>
    new OllamaApiClient(services.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaEmbeddings"),
        ollamaOptions.Model));
builder.Services.AddSingleton<EmbeddingIndexer>();
var searchOptions = builder.Configuration.GetSection("SemanticSearch").Get<SemanticSearchOptions>() ?? new();
searchOptions.Validate();
builder.Services.AddSingleton(searchOptions);
builder.Services.AddSingleton<SemanticSearchService>();
var generationOptions = builder.Configuration.GetSection("Generation").Get<GenerationOptions>() ?? new();
generationOptions.Validate();
builder.Services.AddSingleton(generationOptions);
builder.Services.AddHttpClient("OllamaGeneration", client =>
{
    client.BaseAddress = new Uri(generationOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(generationOptions.TimeoutSeconds);
});
builder.Services.AddSingleton<IChatClient>(services =>
    new OllamaApiClient(services.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaGeneration"), generationOptions.Model));
builder.Services.AddSingleton<EvidenceAnalysisService>();
builder.Services.AddSingleton<RagService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapPost("/api/consultas", async (ConsultaRequest request, RagService rag, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Pergunta))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["pergunta"] = ["Informe uma pergunta."]
        });
    }

    if (request.Pergunta.Length > 2000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["pergunta"] = ["A pergunta deve ter no máximo 2000 caracteres."]
        });
    }

    var result = await rag.ConsultAsync(request.Pergunta, cancellationToken);
    return result.Response is not null ? Results.Ok(result.Response)
        : Results.Problem(statusCode: result.StatusCode, title: "Não foi possível concluir a consulta", detail: result.Error);
})
.WithName("CriarConsulta")
.Produces<ConsultaResponse>()
.ProducesValidationProblem();

app.Run();
