using Folio.Domain.Model;

namespace Folio.Domain.Configuration;

/// <summary><c>project.toml</c>, parsed.</summary>
/// <param name="Version">The schema version the file declares.</param>
/// <param name="Slug">The declared slug, or <see langword="null" /> to default from the directory.</param>
/// <param name="Status">How active the project is.</param>
/// <param name="Role">The part played in it.</param>
/// <param name="Started">When work began, as <c>YYYY</c> or <c>YYYY-MM</c>.</param>
/// <param name="Ended">When work concluded.</param>
/// <param name="Tags">Applied tag ids, before vocabulary checking.</param>
/// <param name="Media">Media references keyed by role, such as <c>hero</c>.</param>
/// <param name="Links">Project links, in declaration order.</param>
/// <param name="Relations">Declared relations, in declaration order.</param>
/// <param name="Sections">Declared sections, in declaration order.</param>
internal sealed record ProjectConfig(
    long Version,
    string? Slug,
    ProjectStatus? Status,
    ProjectRole? Role,
    string? Started,
    string? Ended,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Media,
    IReadOnlyList<LinkEntry> Links,
    IReadOnlyList<RelationEntry> Relations,
    IReadOnlyList<ProseSectionEntry> Sections)
{
    /// <summary>Gets a configuration for a project that ships no <c>project.toml</c>.</summary>
    public static ProjectConfig Absent { get; } = new(
        Version: SchemaVersion.Current,
        Slug: null,
        Status: null,
        Role: null,
        Started: null,
        Ended: null,
        Tags: [],
        Media: new Dictionary<string, string>(StringComparer.Ordinal),
        Links: [],
        Relations: [],
        Sections: []);
}

/// <summary>One project link.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
internal sealed record LinkEntry(LinkType Type, Uri Url);

/// <summary>One declared relation, before its target is checked.</summary>
/// <param name="Type">How the two projects relate.</param>
/// <param name="Target">The target slug.</param>
internal sealed record RelationEntry(RelationType Type, string Target);
