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

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version" };

    private static readonly HashSet<string> Tables = new(StringComparer.Ordinal) { "media" };

    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal) { "actions" };

    /// <summary>Builds the path a section's data file sits at.</summary>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <returns>The repo-relative path.</returns>
    public static string PathFor(string folioRoot, string id) => $"{folioRoot}/sections/{id}.toml";

    /// <summary>Reads one hero's data.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The hero, or <see langword="null" /> if its file is missing or unparseable.</returns>
    public static HeroConfig? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        string path = PathFor(folioRoot, id);
        DiagnosticSink file = sink.ForFile(path);

        if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
        {
            file.Warning(
                DiagnosticCodes.SectionDataUnreadable,
                $"Section '{id}' declares no '{path}'; it was dropped.");
            return null;
        }

        if (!TomlDocumentReader.TryParse(contents, DiagnosticCodes.SectionDataUnreadable, file, out TomlDocumentReader document))
        {
            return null;
        }

        if (!SchemaVersion.TryRead(document, file, out _))
        {
            return null;
        }

        document.ReportUnknownStructure(RootKeys, Tables, Arrays, file);

        return new HeroConfig(Actions(document, id, file), Media(document, file));
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
