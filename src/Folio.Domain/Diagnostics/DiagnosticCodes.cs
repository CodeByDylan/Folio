using System.Collections.Frozen;
using System.Reflection;

namespace Folio.Domain.Diagnostics;

/// <summary>Every diagnostic code the resolver can emit.</summary>
public static class DiagnosticCodes
{
    /// <summary>The central config is absent.</summary>
    public const string CentralMissing = "central.missing";

    /// <summary>The central config could not be parsed.</summary>
    public const string CentralUnparseable = "central.unparseable";

    /// <summary><c>default_locale</c> is not among the declared locales.</summary>
    public const string CentralDefaultLocaleUndeclared = "central.default_locale_undeclared";

    /// <summary>Two projects resolved to the same slug.</summary>
    public const string CentralDuplicateSlug = "central.duplicate_slug";

    /// <summary>A <c>project.toml</c> could not be parsed.</summary>
    public const string ProjectUnparseable = "project.unparseable";

    /// <summary>A listed repository or path does not exist.</summary>
    public const string ProjectNotFound = "project.not_found";

    /// <summary>An authored slug is malformed, or none could be derived.</summary>
    public const string ProjectSlugInvalid = "project.slug_invalid";

    /// <summary>The repository listing exceeded the tree API's limit.</summary>
    public const string ProjectTreeTruncated = "project.tree_truncated";

    /// <summary>A project contributes no sections.</summary>
    public const string ProjectNoSections = "project.no_sections";

    /// <summary>A README was lifted as a section.</summary>
    public const string ProjectReadmeUsed = "project.readme_used";

    /// <summary><c>use_readme</c> was set on a project that declares sections.</summary>
    public const string ProjectReadmeIgnored = "project.readme_ignored";

    /// <summary>A config declares a schema version newer than the parser.</summary>
    public const string SchemaVersionUnsupportedHigh = "schema.version_unsupported_high";

    /// <summary>A config declares a schema version below the supported window.</summary>
    public const string SchemaVersionUnsupportedLow = "schema.version_unsupported_low";

    /// <summary>A config carries no <c>version</c> key.</summary>
    public const string SchemaVersionMissing = "schema.version_missing";

    /// <summary>A config carries a key the parser does not know.</summary>
    public const string SchemaUnknownKey = "schema.unknown_key";

    /// <summary>A config carries an enum value the parser does not know.</summary>
    public const string SchemaUnknownValue = "schema.unknown_value";

    /// <summary>A config carries a value that is malformed for its key.</summary>
    public const string SchemaInvalidValue = "schema.invalid_value";

    /// <summary>One aggregate report of every project still on an older schema version.</summary>
    public const string SchemaVersionLagging = "schema.version_lagging";

    /// <summary>A content directory names a locale the site does not declare.</summary>
    public const string LocaleContentDirUndeclared = "locale.content_dir_undeclared";

    /// <summary>A locale file names a locale the site does not declare in canonical form.</summary>
    public const string LocaleFileUndeclared = "locale.file_undeclared";

    /// <summary>A locale file could not be parsed.</summary>
    public const string LocaleUnparseable = "locale.unparseable";

    /// <summary>A locale file is missing a key present in the default locale.</summary>
    public const string LocaleKeyMissing = "locale.key_missing";

    /// <summary>A locale file carries a key no config references.</summary>
    public const string LocaleKeyOrphaned = "locale.key_orphaned";

    /// <summary>A locale was resolved by stripping subtags.</summary>
    public const string LocaleTruncated = "locale.truncated";

    /// <summary>A declared locale has no content anywhere.</summary>
    public const string LocaleEmpty = "locale.empty";

    /// <summary>A declared section has no file in any locale.</summary>
    public const string SectionMissingAllLocales = "section.missing_all_locales";

    /// <summary>A section file exists somewhere, but not in one locale's whole fallback chain.</summary>
    public const string SectionMissingChain = "section.missing_chain";

    /// <summary>A declared section has no file in one locale.</summary>
    public const string SectionMissingLocale = "section.missing_locale";

    /// <summary>A section file exists but is empty.</summary>
    public const string SectionEmpty = "section.empty";

    /// <summary>A section body carries a second H1 after its title.</summary>
    public const string SectionBodyH1 = "section.body_h1";

    /// <summary>A declared section is on no page, so nothing renders it.</summary>
    public const string SectionUnreferenced = "section.unreferenced";

    /// <summary>A page slug is not a well-formed slug.</summary>
    public const string PageSlugInvalid = "page.slug_invalid";

    /// <summary>A page slug is declared more than once.</summary>
    public const string PageDuplicateSlug = "page.duplicate_slug";

    /// <summary>A page lists a section that is not declared.</summary>
    public const string PageUnknownSection = "page.unknown_section";

    /// <summary>No page is marked as the site's entry point.</summary>
    public const string PageNoHome = "page.no_home";

    /// <summary>More than one page is marked as the site's entry point.</summary>
    public const string PageDuplicateHome = "page.duplicate_home";

    /// <summary>A section's markdown could not be parsed.</summary>
    public const string MarkdownUnparseable = "markdown.unparseable";

    /// <summary>Raw HTML was removed from a section body.</summary>
    public const string MarkdownHtmlStripped = "markdown.html_stripped";

    /// <summary>A relative markdown link matched no declared section.</summary>
    public const string MarkdownLinkUnresolved = "markdown.link_unresolved";

    /// <summary>A link to a sibling section carried a fragment the section anchor cannot keep.</summary>
    public const string MarkdownFragmentDropped = "markdown.fragment_dropped";

    /// <summary>A link's host differs from the site's only by a <c>www.</c> prefix.</summary>
    public const string MarkdownHostNearMatch = "markdown.host_near_match";

    /// <summary>A project applied a tag that is not in the central vocabulary.</summary>
    public const string TagsUnknown = "tags.unknown";

    /// <summary>A relation named a target that is not in the portfolio.</summary>
    public const string RelationsTargetUnknown = "relations.target_unknown";

    /// <summary>A referenced media file does not exist at the pinned SHA.</summary>
    public const string MediaNotFound = "media.not_found";

    /// <summary>An image header could not be recognized.</summary>
    public const string MediaDimensionsUnreadable = "media.dimensions_unreadable";

    /// <summary>Media hosted outside the repository, so not measured.</summary>
    public const string MediaDimensionsExternal = "media.dimensions_external";

    /// <summary>The portfolio contains no projects.</summary>
    public const string PortfolioEmpty = "portfolio.empty";

    /// <summary>A transient fault ended the build; the last good result keeps serving.</summary>
    public const string RefreshAbandoned = "refresh.abandoned";

    /// <summary>Too little API budget remained to complete a build.</summary>
    public const string RefreshRateLimitInsufficient = "refresh.rate_limit_insufficient";

    /// <summary>Gets every declared code.</summary>
    public static FrozenSet<string> All { get; } = typeof(DiagnosticCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToFrozenSet(StringComparer.Ordinal);
}
