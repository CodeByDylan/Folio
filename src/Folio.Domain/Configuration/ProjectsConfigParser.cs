using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a projects section.</summary>
internal static class ProjectsConfigParser
{
    private static readonly HashSet<string> RootKeys =
        new(StringComparer.Ordinal) { "version", "featured", "limit" };

    private static readonly HashSet<string> None = new(StringComparer.Ordinal);

    /// <summary>Reads one projects section's selection.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The selection, or <see langword="null" /> if the file is missing or unparseable.</returns>
    public static ProjectsConfig? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
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

        document.ReportUnknownStructure(RootKeys, None, None, file);

        long? declared = document.Root.Integer("limit", file);
        int? limit = null;

        if (declared is { } value)
        {
            if (value < 1 || value > int.MaxValue)
            {
                file.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"'limit' on '{id}' must be a positive whole number; it was ignored.",
                    document.Root.PositionOf("limit"));
            }
            else
            {
                limit = (int)value;
            }
        }

        return new ProjectsConfig(document.Root.Boolean("featured", file) ?? false, limit);
    }
}
