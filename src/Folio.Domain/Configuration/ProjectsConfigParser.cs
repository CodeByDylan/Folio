using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a projects section.</summary>
internal static class ProjectsConfigParser
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "featured", "limit" };

    /// <summary>Reads one projects section's selection.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The projects section, or <see langword="null" /> if the file is missing or unparseable.</returns>
    public static ProjectsSectionEntry? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        if (SectionDataFile.Read(files, folioRoot, id, Keys, SectionDataFile.None, SectionDataFile.None, sink) is not { } data)
        {
            return null;
        }

        TomlDocumentReader document = data.Document;
        DiagnosticSink file = data.Sink;

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

        return new ProjectsSectionEntry(id, document.Root.Boolean("featured", file) ?? false, limit);
    }
}
