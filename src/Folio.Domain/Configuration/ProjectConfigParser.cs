using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses one repository's <c>project.toml</c>; a failure drops that project only.</summary>
internal sealed class ProjectConfigParser
{
    private static readonly HashSet<string> ProjectKeys = new(StringComparer.Ordinal)
        { "slug", "status", "role", "started", "ended", "tags" };

    private static readonly HashSet<string> LinkKeys = new(StringComparer.Ordinal) { "type", "url" };

    private static readonly HashSet<string> RelationKeys = new(StringComparer.Ordinal) { "type", "target" };

    private static readonly HashSet<string> SectionKeys = new(StringComparer.Ordinal) { "id", "file" };

    private static readonly HashSet<string> MediaKeys = new(StringComparer.Ordinal) { "hero", "hero_dark" };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version" };

    private static readonly HashSet<string> Tables = new(StringComparer.Ordinal) { "project", "project.media" };

    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal) { "links", "relations", "sections" };

    /// <summary>Parses <c>project.toml</c>, treating its absence as an empty configuration.</summary>
    /// <param name="files">The repository's file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory within the repository.</param>
    /// <param name="sink">A sink scoped to the project.</param>
    /// <param name="config">The parsed configuration.</param>
    /// <returns><see langword="false" /> if the project must be dropped.</returns>
    public bool TryParse(FileSet files, string folioRoot, DiagnosticSink sink, out ProjectConfig config)
    {
        config = ProjectConfig.Absent;
        string path = $"{folioRoot}/project.toml";

        if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
        {
            return true;
        }

        DiagnosticSink file = sink.ForFile(path);

        if (!TomlDocumentReader.TryParse(
                contents,
                DiagnosticCodes.ProjectUnparseable,
                file,
                out TomlDocumentReader document))
        {
            return false;
        }

        if (!SchemaVersion.TryRead(document, file, out long version))
        {
            return false;
        }

        document.ReportUnknownStructure(RootKeys, Tables, Arrays, file);

        TomlTableReader? project = document.Table("project");
        project?.ReportUnknownKeys(ProjectKeys, file);

        Dictionary<string, string> media = new(StringComparer.Ordinal);

        TomlTableReader? mediaTable = document.Table("project.media");
        mediaTable?.ReportUnknownKeys(MediaKeys, file);

        foreach (TomlEntry entry in mediaTable?.Entries ?? [])
        {
            if (!MediaKeys.Contains(entry.Key))
            {
                continue;
            }

            string? reference = mediaTable!.String(entry.Key, file);

            if (reference is not null)
            {
                media[entry.Key] = reference;
            }
        }

        config = new ProjectConfig(
            version,
            project?.String("slug", file),
            project is null ? null : EnumNames.Status(project, "status", file),
            project is null ? null : EnumNames.Role(project, "role", file),
            ReadDate(project, "started", file),
            ReadDate(project, "ended", file),
            project?.StringArray("tags", file) ?? [],
            media,
            ReadLinks(document, file),
            ReadRelations(document, file),
            CentralConfigParser.ReadSections(document, "sections", file, SectionKeys));

        return true;
    }

    private static string? ReadDate(TomlTableReader? table, string key, DiagnosticSink sink)
    {
        string? value = table?.String(key, sink);

        if (value is null)
        {
            return null;
        }

        if (IsPartialDate(value))
        {
            return value;
        }

        sink.Warning(
            DiagnosticCodes.SchemaInvalidValue,
            $"'project.{key}' must be YYYY or YYYY-MM; '{value}' was ignored.",
            table!.PositionOf(key));

        return null;
    }

    private static bool IsPartialDate(string value)
    {
        ReadOnlySpan<char> span = value;

        if (span.Length == 4)
        {
            return span.ContainsAnyExceptInRange('0', '9') is false;
        }

        return span.Length == 7
            && span[4] == '-'
            && !span[..4].ContainsAnyExceptInRange('0', '9')
            && !span[5..].ContainsAnyExceptInRange('0', '9')
            && int.Parse(span[5..]) is >= 1 and <= 12;
    }

    private static List<LinkEntry> ReadLinks(TomlDocumentReader document, DiagnosticSink sink)
    {
        List<LinkEntry> links = [];

        foreach (TomlTableReader entry in document.TableArray("links"))
        {
            entry.ReportUnknownKeys(LinkKeys, sink);
            LinkType? type = EnumNames.Link(entry, "type", sink);
            string? url = entry.String("url", sink);

            if (type is null)
            {
                continue;
            }

            if (!LinkTarget.IsWebUrl(url, out Uri target))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"A link's 'url' ({url ?? "absent"}) is not an absolute http or https URL; it was dropped.",
                    entry.PositionOf("url"));
                continue;
            }

            links.Add(new LinkEntry(type.Value, target));
        }

        return links;
    }

    private static List<RelationEntry> ReadRelations(TomlDocumentReader document, DiagnosticSink sink)
    {
        List<RelationEntry> relations = [];

        foreach (TomlTableReader entry in document.TableArray("relations"))
        {
            entry.ReportUnknownKeys(RelationKeys, sink);
            RelationType? type = EnumNames.Relation(entry, "type", sink);
            string? target = entry.String("target", sink);

            if (type is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    "A relation has no 'target'; it was dropped.",
                    entry.Position);
                continue;
            }

            relations.Add(new RelationEntry(type.Value, target));
        }

        return relations;
    }
}
