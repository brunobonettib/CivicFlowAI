using System.Text.Json;
using System.Text.Json.Nodes;
using CivicFlowAI.Api.Knowledge;

namespace CivicFlowAI.Api.Tests;

public sealed class KnowledgeBaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "civicflow-tests-" + Guid.NewGuid());

    public KnowledgeBaseTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "doc-1.md"), "# Informação\nConteúdo oficial de teste.");
        WriteCatalog(new JsonArray(ValidEntry()));
    }

    [Fact]
    public void LoadsRealBaseWithThreeIdentifiableDocuments()
    {
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        Assert.Equal(3, knowledge.Documents.Count);
        Assert.Equal(3, knowledge.Documents.Select(d => d.Metadata.Id).Distinct().Count());
        Assert.All(knowledge.Documents, doc =>
        {
            Assert.Contains(doc.Metadata.Id, doc.Conteudo);
            Assert.Contains(doc.Metadata.Url.AbsoluteUri, doc.Conteudo);
            Assert.Equal(new DateOnly(2026, 11, 5), doc.Metadata.DataConsulta);
        });
    }

    [Fact]
    public void AllowsUnknownSourceUpdateDateAndPreservesText()
    {
        var entry = ValidEntry();
        entry["dataAtualizacao"] = null;
        WriteCatalog(new JsonArray(entry));
        var doc = Assert.Single(KnowledgeBase.Load(directory).Documents);
        Assert.Null(doc.Metadata.DataAtualizacao);
        Assert.Equal("# Informação\nConteúdo oficial de teste.", doc.Conteudo);
    }

    [Theory]
    [InlineData("id", "../outside")]
    [InlineData("id", "")]
    [InlineData("titulo", " ")]
    [InlineData("orgao", "")]
    [InlineData("url", "https://gov.br.example.org/fake")]
    [InlineData("url", "https://fakegov.br/page")]
    [InlineData("url", "http://www.gov.br/page")]
    [InlineData("url", "/relative")]
    [InlineData("url", null)]
    [InlineData("licenca", "")]
    [InlineData("dataConsulta", "0001-01-01")]
    [InlineData("dataConsulta", "invalid")]
    [InlineData("dataAtualizacao", "2026-11-06")]
    [InlineData("dataAtualizacao", "0001-01-01")]
    public void RejectsInvalidMetadata(string key, string? value)
    {
        var entry = ValidEntry();
        entry[key] = value;
        WriteCatalog(new JsonArray(entry));
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("{")]
    [InlineData("[{}]")]
    public void RejectsInvalidCatalog(string json)
    {
        File.WriteAllText(Path.Combine(directory, "catalog.json"), json);
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    [Fact]
    public void RejectsDuplicateIds()
    {
        WriteCatalog(new JsonArray(ValidEntry(), ValidEntry()));
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    [Theory]
    [InlineData("catalog.json")]
    [InlineData("doc-1.md")]
    public void RejectsMissingFiles(string name)
    {
        File.Delete(Path.Combine(directory, name));
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    [Fact]
    public void RejectsEmptyDocument()
    {
        File.WriteAllText(Path.Combine(directory, "doc-1.md"), " \n");
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    [Fact]
    public void RejectsUnlistedDocument()
    {
        File.WriteAllText(Path.Combine(directory, "orphan.md"), "Sem metadados");
        Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(directory));
    }

    private void WriteCatalog(JsonArray catalog) =>
        File.WriteAllText(Path.Combine(directory, "catalog.json"), catalog.ToJsonString());

    private static JsonObject ValidEntry() => new()
    {
        ["id"] = "doc-1", ["titulo"] = "Documento", ["orgao"] = "Órgão",
        ["url"] = "https://www.gov.br/documento", ["dataConsulta"] = "2026-11-05",
        ["dataAtualizacao"] = "2026-07-01", ["licenca"] = "https://creativecommons.org/licenses/by-nd/3.0/"
    };

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
