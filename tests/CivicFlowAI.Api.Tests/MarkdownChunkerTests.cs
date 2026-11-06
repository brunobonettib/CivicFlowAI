using System.Text.RegularExpressions;
using CivicFlowAI.Api.Knowledge;

namespace CivicFlowAI.Api.Tests;

public sealed class MarkdownChunkerTests
{
    [Fact]
    public void PreservesHashInHeadingTextAndRemovesClosingHeadingMarkers()
    {
        var chunks = Chunker().Split(Document("# Guia ##\n## C#\nTexto.\n## Outro ###\nMais texto."));
        Assert.Equal(new[] { "Guia > C#", "Guia > Outro" }, chunks.Select(c => c.Secao));
    }

    [Fact]
    public void KeepsSectionsAndSourceAssociation()
    {
        var document = Document("# Guia\n## Registro\nPrimeiro parágrafo.\n\nSegundo parágrafo.\n### Detalhes\nUm detalhe.\n## Prazos\nAté vinte dias.");
        var chunks = Chunker().Split(document);
        Assert.Equal(new[] { "Guia > Registro", "Guia > Registro > Detalhes", "Guia > Prazos" }, chunks.Select(c => c.Secao));
        Assert.Equal("Primeiro parágrafo.\n\nSegundo parágrafo.", chunks[0].Conteudo);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(document.Metadata.Id, chunk.DocumentoId);
            Assert.Equal(document.Metadata.Titulo, chunk.Titulo);
            Assert.Equal(document.Metadata.Url, chunk.Url);
            Assert.DoesNotContain("#", chunk.Conteudo);
        });
    }

    [Fact]
    public void RemovesPreambleMetadataButPreservesContentLabels()
    {
        var chunks = Chunker().Split(Document("# Guia\n\nID: doc-1\n\nFonte: órgão\nLicença indicada na fonte: licença\n\n## Informações\nFonte: registro público.\n\nNota editorial: explicação relevante."));
        var chunk = Assert.Single(chunks);
        Assert.Equal("Fonte: registro público.\n\nNota editorial: explicação relevante.", chunk.Conteudo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("# Título\n\n## Seção vazia")]
    [InlineData("# Título\nID: doc-1\nFonte: órgão\nLicença indicada na fonte: licença")]
    public void SkipsEmptyContent(string text) => Assert.Empty(Chunker().Split(Document(text)));

    [Fact]
    public void KeepsIntroWithoutHeading()
    {
        var chunk = Assert.Single(Chunker().Split(Document("Texto introdutório.")));
        Assert.Equal("Documento de teste", chunk.Secao);
        Assert.Equal("Texto introdutório.", chunk.Conteudo);
    }

    [Fact]
    public void PacksParagraphsIncludingSeparatorInTarget()
    {
        var document = Document("Primeiro.\n\nSegundo.");
        Assert.Single(Chunker(19).Split(document));
        var chunks = Chunker(18).Split(document);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.InRange(c.Conteudo.Length, 1, 18));
    }

    [Fact]
    public void SplitsLongParagraphAtSentencesWithoutLosingOrDuplicatingText()
    {
        const string body = "Primeira frase completa. Segunda frase completa! Terceira frase completa? Quarta frase completa.";
        var chunks = Chunker(50).Split(Document(body));
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.InRange(c.Conteudo.Length, 1, 50));
        Assert.Equal(Normalize(body), Normalize(string.Join(' ', chunks.Select(c => c.Conteudo))));
        Assert.Equal("Primeira frase completa.\n\nSegunda frase completa!", chunks[0].Conteudo);
    }

    [Fact]
    public void KeepsListItemsAndContinuationsTogether()
    {
        const string first = "- Um item muito longo. Outra frase que pertence ao mesmo item.\n  Continuação do item.";
        var chunks = Chunker(30).Split(Document(first + "\n- Outro item."));
        Assert.Equal(2, chunks.Count);
        Assert.Equal(first, chunks[0].Conteudo);
        Assert.Equal("- Outro item.", chunks[1].Conteudo);
    }

    [Fact]
    public void KeepsIndivisibleSentenceBeyondTargetAndIsolatesIt()
    {
        var longSentence = new string('á', 1100) + ".";
        var chunks = Chunker().Split(Document("Antes.\n\n" + longSentence + "\n\nDepois."));
        Assert.Equal(new[] { "Antes.", longSentence, "Depois." }, chunks.Select(c => c.Conteudo));
    }

    [Fact]
    public void PreservesAbbreviationsUrlsAndNumbers()
    {
        const string first = "O art. Onze cita o Dr. Silva e https://www.gov.br/teste com 7.724/2012.";
        var chunks = Chunker(40).Split(Document(first + " Próxima frase."));
        Assert.Equal(new[] { first, "Próxima frase." }, chunks.Select(c => c.Conteudo));
    }

    [Fact]
    public void IdsAreDeterministicUniqueAndIndependentOfUnrelatedSections()
    {
        var document = Document("## Uma\nTexto repetido.\n\nTexto repetido.\n## Outra\nOutro texto.");
        var chunker = Chunker(20);
        var chunks = chunker.Split(document);
        Assert.Equal(chunks.ToArray(), chunker.Split(document).ToArray());
        Assert.Equal(3, chunks.Select(c => c.Id).Distinct().Count());
        Assert.Equal(chunks.ToArray(), chunker.Split(document with { Conteudo = document.Conteudo.Replace("\n", "\r\n") }).ToArray());
        var extended = chunker.Split(document with { Conteudo = "## Nova\nTexto anterior.\n" + document.Conteudo });
        Assert.Equal(chunks.Select(c => c.Id), extended.Skip(1).Select(c => c.Id));
        var changedSource = chunker.Split(document with { Metadata = document.Metadata with { Id = "doc-2" } });
        Assert.Empty(chunks.Select(c => c.Id).Intersect(changedSource.Select(c => c.Id)));
        Assert.NotEqual(chunks[0].Id, chunker.Split(Document("## Uma\nTexto alterado."))[0].Id);
        Assert.NotEqual(chunks[0].Id, chunker.Split(Document("## Outra\nTexto repetido."))[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidTarget(int target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Chunker(target));

    [Theory]
    [InlineData(1000)]
    [InlineData(250)]
    public void ProcessesAllRealDocumentsPreservingEntireBody(int target)
    {
        var knowledge = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));
        var all = new List<KnowledgeChunk>();
        Assert.Equal(3, knowledge.Documents.Count);
        foreach (var document in knowledge.Documents)
        {
            var chunks = Chunker(target).Split(document);
            Assert.NotEmpty(chunks);
            // As fichas atuais têm uma seção de corpo após o preâmbulo de atribuição.
            var lines = document.Conteudo.ReplaceLineEndings("\n").Split('\n');
            var body = string.Join('\n', lines.SkipWhile(line => !line.StartsWith("## ")).Skip(1));
            Assert.Equal(Normalize(body), Normalize(string.Join(' ', chunks.Select(c => c.Conteudo))));
            Assert.All(chunks, chunk =>
            {
                Assert.Equal(document.Metadata.Id, chunk.DocumentoId);
                Assert.Equal(document.Metadata.Titulo, chunk.Titulo);
                Assert.Equal(document.Metadata.Url, chunk.Url);
                Assert.InRange(chunk.Conteudo.Length, 1, target);
                Assert.DoesNotContain("Ficha factual", chunk.Conteudo);
                Assert.DoesNotContain("Licença indicada", chunk.Conteudo);
                Assert.DoesNotContain("Consulta: 2026", chunk.Conteudo);
            });
            all.AddRange(chunks);
        }
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    private static MarkdownChunker Chunker(int target = 1000) => new(new ChunkingOptions { TargetCharacters = target });
    private static KnowledgeDocument Document(string text) => new(
        new KnowledgeMetadata("doc-1", "Documento de teste", "Órgão", new Uri("https://www.gov.br/teste"),
            new DateOnly(2026, 11, 5), null, new Uri("https://creativecommons.org/licenses/by-nd/3.0/")), text);
}
