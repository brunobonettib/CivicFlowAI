using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;

namespace CivicFlowAI.Api.Knowledge;

public sealed class SemanticSearchService
{
    private readonly EmbeddingIndexer indexer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;
    private readonly int topK;
    private readonly TimeSpan timeout;

    public SemanticSearchService(EmbeddingIndexer indexer,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        SemanticSearchOptions searchOptions, OllamaEmbeddingOptions ollamaOptions)
    {
        searchOptions.Validate();
        ollamaOptions.Validate();
        this.indexer = indexer;
        this.generator = generator;
        topK = searchOptions.TopK;
        timeout = TimeSpan.FromSeconds(ollamaOptions.TimeoutSeconds);
    }

    public async Task<SemanticSearchResult> SearchAsync(string? question, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(question) || question.Length > 2000)
                return new(SearchStatus.InvalidQuestion, [], "Informe uma pergunta de até 2000 caracteres.");

            // Uma única versão do índice por consulta, mesmo se houver reindexação concorrente.
            var index = indexer.Current;
            if (index.Items.Count == 0)
                return new(SearchStatus.EmptyIndex, [], "O índice ainda não contém documentos.");
            if (string.IsNullOrWhiteSpace(index.Model) || index.Dimensions <= 0)
                return new(SearchStatus.InvalidEmbeddings, [], "Metadados do índice inválidos.");

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            var token = deadline.Token;
            var embeddings = await generator.GenerateAsync([question.Trim()], new EmbeddingGenerationOptions
            {
                ModelId = index.Model,
                AdditionalProperties = new() { ["truncate"] = false }
            }, token);
            token.ThrowIfCancellationRequested();

            if (embeddings is null || embeddings.Count != 1 || embeddings[0] is null
                || embeddings[0].Vector.Length != index.Dimensions)
                throw new InvalidDataException("Embedding da pergunta ausente ou com dimensão incompatível.");

            var query = embeddings[0].Vector;
            var hits = new List<SemanticSearchHit>(index.Items.Count);
            foreach (var item in index.Items)
            {
                token.ThrowIfCancellationRequested();
                hits.Add(new SemanticSearchHit(item.Chunk, CosineSimilarity(query.Span, item.Vector)));
            }

            var results = hits.OrderByDescending(hit => hit.Score)
                .ThenBy(hit => hit.Chunk.Id, StringComparer.Ordinal).Take(topK).ToList().AsReadOnly();
            token.ThrowIfCancellationRequested();
            return new(SearchStatus.Completed, results);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? new(SearchStatus.Cancelled, [], "Busca cancelada.")
                : new(SearchStatus.Unavailable, [], "Tempo limite ao gerar embedding da pergunta.");
        }
        catch (InvalidDataException exception)
        {
            return new(SearchStatus.InvalidEmbeddings, [], exception.Message);
        }
        catch (JsonException)
        {
            return new(SearchStatus.InvalidEmbeddings, [], "Resposta de embeddings inválida.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OllamaException or IOException)
        {
            return new(SearchStatus.Unavailable, [], "Ollama indisponível ou modelo não pôde gerar embedding.");
        }
    }

    private static double CosineSimilarity(ReadOnlySpan<float> query, IReadOnlyList<float> vector)
    {
        if (query.Length == 0 || query.Length != vector.Count)
            throw new InvalidDataException("Vetores vazios ou com dimensões incompatíveis.");

        double dot = 0, queryNorm = 0, documentNorm = 0;
        for (var i = 0; i < query.Length; i++)
        {
            if (!float.IsFinite(query[i]) || !float.IsFinite(vector[i]))
                throw new InvalidDataException("Vetor contém valores não finitos.");
            // Acumulação em double evita overflow/underflow dos produtos de valores float.
            dot += (double)query[i] * vector[i];
            queryNorm += (double)query[i] * query[i];
            documentNorm += (double)vector[i] * vector[i];
        }

        if (queryNorm == 0 || documentNorm == 0)
            throw new InvalidDataException("Similaridade de cosseno indefinida para vetor de norma zero.");

        return Math.Clamp(dot / (Math.Sqrt(queryNorm) * Math.Sqrt(documentNorm)), -1d, 1d);
    }
}
