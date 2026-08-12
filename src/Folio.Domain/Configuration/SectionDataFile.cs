using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>A section's data file, read and checked against the shape its type declares.</summary>
/// <param name="Document">The parsed file.</param>
/// <param name="Sink">A sink already scoped to it.</param>
internal sealed record SectionData(TomlDocumentReader Document, DiagnosticSink Sink);

/// <summary>Reads <c>sections/&lt;id&gt;.toml</c>, which every typed section but prose and contact declares.</summary>
internal static class SectionDataFile
{
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version" };

    /// <summary>No table or table array, for a type that declares none.</summary>
    public static readonly HashSet<string> None = new(StringComparer.Ordinal);

    /// <summary>Builds the path a section's data file sits at.</summary>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <returns>The repo-relative path.</returns>
    public static string PathFor(string folioRoot, string id) => $"{folioRoot}/sections/{id}.toml";

    /// <summary>Reads one section's data file and reports anything its schema does not name.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="keys">The root keys the type declares, beside <c>version</c>.</param>
    /// <param name="tables">The tables the type declares.</param>
    /// <param name="arrays">The table arrays the type declares.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The file, or <see langword="null" /> if it is missing or unusable.</returns>
    public static SectionData? Read(
        FileSet files,
        string folioRoot,
        string id,
        IReadOnlySet<string> keys,
        IReadOnlySet<string> tables,
        IReadOnlySet<string> arrays,
        DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(files);

        string path = PathFor(folioRoot, id);
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

        document.ReportUnknownStructure(
            keys.Count == 0 ? RootKeys : new HashSet<string>(keys, StringComparer.Ordinal) { "version" },
            tables,
            arrays,
            file);

        return new SectionData(document, file);
    }
}
