using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CivicFlowAI.Api.Knowledge;

public sealed class KnowledgeBase
{
    public IReadOnlyList<KnowledgeDocument> Documents { get; }

    private KnowledgeBase(List<KnowledgeDocument> documents) => Documents = documents.AsReadOnly();

    public static KnowledgeBase Load(string directory)
    {
        var encoding = new UTF8Encoding(false, true);
        var catalogPath = Path.Combine(directory, "catalog.json");
        if (!File.Exists(catalogPath))
            throw new InvalidDataException("Catálogo de conhecimento não encontrado.");

        List<KnowledgeMetadata?> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<KnowledgeMetadata?>>(
                File.ReadAllText(catalogPath, encoding), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Catálogo de conhecimento nulo.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("JSON ou metadados do catálogo inválidos.", exception);
        }

        if (entries.Count == 0)
            throw new InvalidDataException("Catálogo de conhecimento vazio.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<KnowledgeDocument>();
        foreach (var metadata in entries)
        {
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Id)
                || !Regex.IsMatch(metadata.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
                || !ids.Add(metadata.Id))
                throw new InvalidDataException("Identificador de documento inválido ou duplicado.");

            if (string.IsNullOrWhiteSpace(metadata.Titulo) || string.IsNullOrWhiteSpace(metadata.Orgao)
                || !IsHttps(metadata.Url)
                || !(metadata.Url.Host.Equals("gov.br", StringComparison.OrdinalIgnoreCase)
                    || metadata.Url.Host.EndsWith(".gov.br", StringComparison.OrdinalIgnoreCase))
                || !IsHttps(metadata.Licenca)
                || metadata.DataConsulta == default
                || metadata.DataAtualizacao == DateOnly.MinValue
                || metadata.DataAtualizacao > metadata.DataConsulta)
                throw new InvalidDataException($"Metadados inválidos: {metadata.Id}.");

            // O nome deriva exclusivamente do ID validado, sem caminhos fornecidos pelo catálogo.
            var path = Path.Combine(directory, metadata.Id + ".md");
            if (!File.Exists(path))
                throw new InvalidDataException($"Documento ausente: {metadata.Id}.");

            var content = File.ReadAllText(path, encoding);
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException($"Documento vazio: {metadata.Id}.");

            documents.Add(new KnowledgeDocument(metadata, content));
        }

        var unlisted = Directory.EnumerateFiles(directory, "*.md")
            .Any(path => !ids.Contains(Path.GetFileNameWithoutExtension(path)));
        if (unlisted)
            throw new InvalidDataException("Documento Markdown sem registro no catálogo.");

        return new KnowledgeBase(documents);
    }

    private static bool IsHttps(Uri? uri) =>
        uri is { IsAbsoluteUri: true, Scheme: "https", UserInfo: "" };
}
