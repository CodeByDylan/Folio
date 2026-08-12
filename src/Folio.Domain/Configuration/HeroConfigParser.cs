using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a hero section.</summary>
internal static class HeroConfigParser
{
    /// <summary>The image roles a hero may declare.</summary>
    internal static readonly HashSet<string> MediaKeys = new(StringComparer.Ordinal) { "image", "image_dark" };

    private static readonly HashSet<string> ActionKeys = new(StringComparer.Ordinal) { "id", "url" };

    private static readonly HashSet<string> Tables = new(StringComparer.Ordinal) { "media" };

    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal) { "actions" };

    /// <summary>Reads one hero's data.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The hero section, or <see langword="null" /> if its file is missing or unparseable.</returns>
    public static HeroSectionEntry? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        if (SectionDataFile.Read(files, folioRoot, id, SectionDataFile.None, Tables, Arrays, sink) is not { } data)
        {
            return null;
        }

        return new HeroSectionEntry(id, Actions(data.Document, id, data.Sink), Media(data.Document, data.Sink));
    }

    private static IReadOnlyList<HeroActionEntry> Actions(
        TomlDocumentReader document,
        string id,
        DiagnosticSink sink)
    {
        List<HeroActionEntry> actions = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (TomlTableReader entry in document.TableArray("actions"))
        {
            entry.ReportUnknownKeys(ActionKeys, sink);
            string? action = entry.String("id", sink);
            string? target = entry.String("url", sink);

            if (string.IsNullOrWhiteSpace(action))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"An action on '{id}' has no 'id'; it was dropped.",
                    entry.Position);
                continue;
            }

            if (!LinkTarget.IsAbsolute(target ?? string.Empty, out Uri url))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Action '{action}' has no absolute 'url'; it was dropped.",
                    entry.PositionOf("url"));
                continue;
            }

            if (!seen.Add(action))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Action id '{action}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            actions.Add(new HeroActionEntry(action, url));
        }

        return actions;
    }

    private static IReadOnlyDictionary<string, string> Media(TomlDocumentReader document, DiagnosticSink sink)
    {
        Dictionary<string, string> media = new(StringComparer.Ordinal);
        TomlTableReader? table = document.Table("media");

        if (table is null)
        {
            return media;
        }

        table.ReportUnknownKeys(MediaKeys, sink);

        foreach (string role in MediaKeys)
        {
            if (table.String(role, sink) is { Length: > 0 } reference)
            {
                media[role] = reference;
            }
        }

        return media;
    }
}
