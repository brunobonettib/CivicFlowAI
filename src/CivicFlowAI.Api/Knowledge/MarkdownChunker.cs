using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CivicFlowAI.Api.Knowledge;

public sealed class MarkdownChunker
{
    private readonly int targetCharacters;

    public MarkdownChunker(ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TargetCharacters);
        targetCharacters = options.TargetCharacters;
    }

    public IReadOnlyList<KnowledgeChunk> Split(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = new List<KnowledgeChunk>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var headings = new string?[6];
        var section = document.Metadata.Titulo;
        var paragraph = new List<string>();
        var current = new StringBuilder();
        var preamble = true;
        var listItem = false;

        void FlushChunk()
        {
            if (current.Length == 0) return;
            var content = current.ToString();
            var identity = JsonSerializer.Serialize(new[] { document.Metadata.Id, section, content });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            var occurrence = occurrences.GetValueOrDefault(hash) + 1;
            occurrences[hash] = occurrence;
            chunks.Add(new KnowledgeChunk($"{document.Metadata.Id}:{hash}:{occurrence}",
                document.Metadata.Id, document.Metadata.Titulo, document.Metadata.Url, section, content));
            current.Clear();
        }

        void AppendUnit(string unit)
        {
            if (current.Length > 0 && current.Length + 2 + unit.Length > targetCharacters)
                FlushChunk();
            if (current.Length > 0) current.Append("\n\n");
            current.Append(unit);
        }

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var text = string.Join('\n', paragraph).Trim();
            if (text.Length > 0)
            {
                // Itens de lista permanecem inteiros, inclusive suas linhas de continuação.
                if (text.Length <= targetCharacters || listItem)
                    AppendUnit(text);
                else
                    foreach (var sentence in SplitSentences(text)) AppendUnit(sentence);
            }
            paragraph.Clear();
            listItem = false;
        }

        foreach (var line in document.Conteudo.ReplaceLineEndings("\n").Split('\n'))
        {
            var heading = Regex.Match(line, @"^ {0,3}(#{1,6})[ \t]+(.+?)(?:[ \t]+#+)?[ \t]*$");
            if (heading.Success)
            {
                FlushParagraph();
                FlushChunk();
                var level = heading.Groups[1].Length;
                headings[level - 1] = heading.Groups[2].Value;
                Array.Clear(headings, level, headings.Length - level);
                section = string.Join(" > ", headings.Where(value => value is not null));
                if (level > 1) preamble = false;
                continue;
            }

            var trimmed = line.Trim();
            // Convenção das fichas locais: apenas estes campos no preâmbulo são metadados.
            if (preamble && (trimmed.StartsWith("ID:", StringComparison.Ordinal)
                || trimmed.StartsWith("Fonte:", StringComparison.Ordinal)
                || trimmed.StartsWith("Licença indicada na fonte:", StringComparison.Ordinal)))
                continue;

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            preamble = false;
            if (Regex.IsMatch(line, @"^\s*(?:[-+*]|\d+[.)])\s+"))
            {
                FlushParagraph();
                listItem = true;
            }
            paragraph.Add(line.TrimEnd());
        }

        FlushParagraph();
        FlushChunk();
        return chunks.AsReadOnly();
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        // Heurística conservadora: pontuação, espaço e início maiúsculo.
        // Não separa dentro de URLs/números; abreviações comuns não encerram frases.
        foreach (Match boundary in Regex.Matches(text, @"(?<=[.!?])\s+(?=\p{Lu})"))
        {
            var preceding = text[start..boundary.Index];
            if (Regex.IsMatch(preceding, @"\b(?:art|arts|sr|sra|dr|dra|etc|ex|inc|pág)\.$", RegexOptions.IgnoreCase))
                continue;
            yield return preceding.Trim();
            start = boundary.Index + boundary.Length;
        }
        var remainder = text[start..].Trim();
        if (remainder.Length > 0) yield return remainder;
    }
}
