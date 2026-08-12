using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a Q&amp;A section.</summary>
internal static class QaConfigParser
{
    private static readonly HashSet<string> EntryKeys = new(StringComparer.Ordinal) { "id" };

    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal) { "entries" };

    /// <summary>Reads one Q&amp;A section's declared entries.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The Q&amp;A section, or <see langword="null" /> if the file is unusable.</returns>
    public static QaSectionEntry? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        if (SectionDataFile.Read(files, folioRoot, id, SectionDataFile.None, SectionDataFile.None, Arrays, sink) is not { } data)
        {
            return null;
        }

        TomlDocumentReader document = data.Document;
        DiagnosticSink file = data.Sink;

        List<string> entries = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (TomlTableReader entry in document.TableArray("entries"))
        {
            entry.ReportUnknownKeys(EntryKeys, file);
            string? question = entry.String("id", file);

            if (string.IsNullOrWhiteSpace(question))
            {
                file.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"An entry on '{id}' has no 'id'; it was dropped.",
                    entry.Position);
                continue;
            }

            if (!seen.Add(question))
            {
                file.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Entry id '{question}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            entries.Add(question);
        }

        return new QaSectionEntry(id, entries);
    }
}
