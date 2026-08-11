using System.Collections.Frozen;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Maps the lowercase, hyphenated names used in TOML onto their enum members.</summary>
internal static class EnumNames
{
    private static readonly FrozenDictionary<string, ProjectStatus> Statuses =
        Map(("wip", ProjectStatus.Wip), ("active", ProjectStatus.Active),
            ("maintenance", ProjectStatus.Maintenance), ("archived", ProjectStatus.Archived));

    private static readonly FrozenDictionary<string, ProjectRole> Roles =
        Map(("author", ProjectRole.Author), ("maintainer", ProjectRole.Maintainer),
            ("contributor", ProjectRole.Contributor));

    private static readonly FrozenDictionary<string, LinkType> LinkTypes =
        Map(("demo", LinkType.Demo), ("docs", LinkType.Docs), ("package", LinkType.Package),
            ("article", LinkType.Article), ("design", LinkType.Design));

    private static readonly FrozenDictionary<string, SiteLinkType> SiteLinkTypes =
        Map(("github", SiteLinkType.GitHub), ("linkedin", SiteLinkType.LinkedIn),
            ("mastodon", SiteLinkType.Mastodon), ("email", SiteLinkType.Email),
            ("website", SiteLinkType.Website));

    private static readonly FrozenDictionary<string, RelationType> RelationTypes = RelationVocabulary.Declarable;

    private static readonly FrozenDictionary<string, SectionType> SectionTypes =
        Map(("prose", SectionType.Prose), ("hero", SectionType.Hero), ("skills", SectionType.Skills),
            ("qa", SectionType.Qa), ("contact", SectionType.Contact),
            ("projects", SectionType.Projects));

    private static readonly FrozenDictionary<string, SkillLevel> SkillLevels =
        Map(("familiar", SkillLevel.Familiar), ("proficient", SkillLevel.Proficient),
            ("expert", SkillLevel.Expert));

    private static readonly FrozenDictionary<string, TagKind> TagKinds =
        Map(("language", TagKind.Language), ("framework", TagKind.Framework),
            ("domain", TagKind.Domain), ("tool", TagKind.Tool));

    /// <summary>Gets the locale key for a page's navigation label.</summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The dotted key its label is declared under.</returns>
    public static string PageKey(Slug slug) => $"page.{slug.Value}";

    /// <summary>Gets the locale key for a link type.</summary>
    /// <param name="type">The link type.</param>
    /// <returns>The dotted key its label is declared under.</returns>
    public static string LinkKey(LinkType type) => $"link.{type.ToString().ToLowerInvariant()}";

    /// <summary>Gets the locale key for a site link type.</summary>
    /// <param name="type">The link type.</param>
    /// <returns>The dotted key its label is declared under.</returns>
    public static string LinkKey(SiteLinkType type) => $"link.{type.ToString().ToLowerInvariant()}";

    /// <summary>Reads a project status.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The status, or <see langword="null" /> if absent or unknown.</returns>
    public static ProjectStatus? Status(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(Statuses, table, key, sink);

    /// <summary>Reads a project role.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The role, or <see langword="null" /> if absent or unknown.</returns>
    public static ProjectRole? Role(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(Roles, table, key, sink);

    /// <summary>Reads a project link type.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The link type, or <see langword="null" /> if absent or unknown.</returns>
    public static LinkType? Link(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(LinkTypes, table, key, sink);

    /// <summary>Reads a site link type.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The link type, or <see langword="null" /> if absent or unknown.</returns>
    public static SiteLinkType? SiteLink(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(SiteLinkTypes, table, key, sink);

    /// <summary>Reads a relation type.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The relation type, or <see langword="null" /> if absent or unknown.</returns>
    public static RelationType? Relation(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(RelationTypes, table, key, sink);

    /// <summary>Reads a section type.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The type, or <see langword="null" /> if absent or unknown.</returns>
    public static SectionType? Section(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(SectionTypes, table, key, sink);

    /// <summary>Reads a skill level.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The level, or <see langword="null" /> if absent or unknown.</returns>
    public static SkillLevel? Level(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(SkillLevels, table, key, sink);

    /// <summary>Reads a tag kind.</summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report an unknown value.</param>
    /// <returns>The kind, or <see langword="null" /> if absent or unknown.</returns>
    public static TagKind? Kind(TomlTableReader table, string key, DiagnosticSink sink) =>
        Read(TagKinds, table, key, sink);

    private static TEnum? Read<TEnum>(
        FrozenDictionary<string, TEnum> names,
        TomlTableReader table,
        string key,
        DiagnosticSink sink)
        where TEnum : struct, Enum
    {
        string? value = table.String(key, sink);

        if (value is null)
        {
            return null;
        }

        if (names.TryGetValue(value, out TEnum parsed))
        {
            return parsed;
        }

        sink.Warning(
            DiagnosticCodes.SchemaUnknownValue,
            $"'{value}' is not a known value for '{key}'; expected one of {string.Join(", ", names.Keys.Order(StringComparer.Ordinal))}.",
            table.PositionOf(key));

        return null;
    }

    private static FrozenDictionary<string, TEnum> Map<TEnum>(params (string Name, TEnum Value)[] entries)
        where TEnum : struct, Enum =>
        entries.ToFrozenDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
