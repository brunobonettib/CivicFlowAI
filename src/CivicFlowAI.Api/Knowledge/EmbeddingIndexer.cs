using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;

namespace CivicFlowAI.Api.Knowledge;

public sealed class EmbeddingIndexer : IDisposable
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;
    private readonly IReadOnlyList<KnowledgeChunk> chunks;
    private readonly OllamaEmbeddingOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private EmbeddingIndex current = new(null, 0, Array.Empty<EmbeddedChunk>());

    public EmbeddingIndex Current => Volatile.Read(ref current);

    public EmbeddingIndexer(IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<KnowledgeChunk> chunks, OllamaEmbeddingOptions options)
    {
        options.Validate();
        this.generator = generator;
        this.chunks = chunks.ToArray();
        this.options = options;
    }

    // Nenhuma chamada ao Ollama no construtor. O consumidor interno deve aguardar esta operação.
    public async Task<IndexingResult> IndexAsync(CancellationToken cancellationToken = default)
    {
        try { await gate.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { return new(IndexingStatus.Cancelled, "Indexação cancelada."); }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var token = timeout.Token;
            var pending = new List<EmbeddedChunk>(chunks.Count);
            var dimensions = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var chunk in chunks)
                if (chunk is null || string.IsNullOrWhiteSpace(chunk.Id)
                    || string.IsNullOrWhiteSpace(chunk.Conteudo) || !ids.Add(chunk.Id))
                    throw new InvalidDataException("Chunks vazios ou IDs duplicados não podem ser indexados.");

            foreach (var batch in chunks.Chunk(options.BatchSize))
            {
                token.ThrowIfCancellationRequested();
                var embeddings = await generator.GenerateAsync(batch.Select(chunk => chunk.Conteudo),
                    new EmbeddingGenerationOptions
                    {
                        ModelId = options.Model,
                        // Chunks indivisíveis podem exceder o contexto: falhar, nunca truncar silenciosamente.
                        AdditionalProperties = new() { ["truncate"] = false }
                    }, token);

                token.ThrowIfCancellationRequested();
                if (embeddings is null || embeddings.Count != batch.Length)
                    throw new InvalidDataException("Quantidade de embeddings diferente da quantidade de chunks.");

                for (var i = 0; i < batch.Length; i++)
                {
                    var embedding = embeddings[i];
                    if (embedding is null || embedding.Vector.Length == 0)
                        throw new InvalidDataException("Embedding vazio.");

                    var vector = embedding.Vector.ToArray();
                    if (dimensions == 0) dimensions = vector.Length;
                    if (vector.Length != dimensions || vector.Any(value => !float.IsFinite(value)))
                        throw new InvalidDataException("Embedding com dimensão inconsistente ou valor não finito.");

                    // A API de embeddings retorna vetores na ordem dos textos enviados.
                    pending.Add(new EmbeddedChunk(batch[i], Array.AsReadOnly(vector)));
                }
            }

            token.ThrowIfCancellationRequested();
            // Publicação atômica: falhas/cancelamento não expõem índice parcial nem apagam o anterior.
            Volatile.Write(ref current, new EmbeddingIndex(options.Model, dimensions, pending.AsReadOnly()));
            return new(IndexingStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? new(IndexingStatus.Cancelled, "Indexação cancelada.")
                : new(IndexingStatus.Unavailable, "Tempo limite ao gerar embeddings no Ollama.");
        }
        catch (InvalidDataException exception)
        {
            return new(IndexingStatus.InvalidEmbeddings, exception.Message);
        }
        catch (JsonException)
        {
            return new(IndexingStatus.InvalidEmbeddings, "Resposta de embeddings inválida.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OllamaException or IOException)
        {
            // Não propaga mensagens do provedor, que podem conter textos de entrada.
            return new(IndexingStatus.Unavailable, "Ollama indisponível ou modelo não pôde gerar embeddings.");
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
