using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a Q&amp;A section.</summary>
internal static class QaConfigParser
{
    private static readonly HashSet<string> EntryKeys = new(StringComparer.Ordinal) { "id" };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version" };

    private static readonly HashSet<string> NoTables = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal) { "entries" };

    /// <summary>Reads one Q&amp;A section's declared entries.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The entry ids in declaration order, or <see langword="null" /> if the file is unusable.</returns>
    public static IReadOnlyList<string>? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        string path = HeroConfigParser.PathFor(folioRoot, id);
        DiagnosticSink file = sink.ForFile(path);

        if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
        {
            file.Warning(
                DiagnosticCodes.SectionDataUnreadable,
                $"Section '{id}' declares no '{path}'; it was dropped.");
            return null;
        }

        if (!TomlDocumentReader.TryParse(
            contents, DiagnosticCodes.SectionDataUnreadable, file, out TomlDocumentReader document))
        {
            return null;
        }

        if (!SchemaVersion.TryRead(document, file, out _))
        {
            return null;
        }

        document.ReportUnknownStructure(RootKeys, NoTables, Arrays, file);

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

        return entries;
    }
}
