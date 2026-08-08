using Folio.Domain.Model;

namespace Folio.Domain.Configuration;

/// <summary>The three central files, parsed.</summary>
/// <param name="Site">Site-level facts.</param>
/// <param name="Projects">The ordered portfolio list.</param>
/// <param name="Tags">The tag vocabulary, keyed by id.</param>
internal sealed record CentralConfig(
    SiteConfig Site,
    IReadOnlyList<ProjectEntry> Projects,
    IReadOnlyDictionary<string, TagDefinition> Tags);

/// <summary><c>site.toml</c>, parsed.</summary>
/// <param name="Url">The origin, and optional path prefix, of the live site.</param>
/// <param name="DefaultLocale">The locale content falls back to.</param>
/// <param name="Locales">Every locale the site publishes.</param>
/// <param name="Owner">The default owner for unqualified repository names.</param>
/// <param name="Links">Site-level links, in declaration order.</param>
/// <param name="Sections">Site-level pages, in declaration order.</param>
internal sealed record SiteConfig(
    Uri Url,
    LocaleTag DefaultLocale,
    IReadOnlyList<LocaleTag> Locales,
    string Owner,
    IReadOnlyList<SiteLinkEntry> Links,
    IReadOnlyList<SectionEntry> Sections);

/// <summary>One entry in <c>projects.toml</c>.</summary>
/// <param name="Repo">The repository, as <c>owner/name</c>.</param>
/// <param name="Path">The subdirectory containing <c>.folio</c>, empty at the repository root.</param>
/// <param name="Ref">The branch, tag or SHA to read from.</param>
/// <param name="IsFeatured">Whether the site highlights this project.</param>
/// <param name="UseReadme">Whether the README may stand in for missing sections.</param>
internal sealed record ProjectEntry(string Repo, string Path, string? Ref, bool IsFeatured, bool UseReadme);

/// <summary>One entry in <c>tags.toml</c>.</summary>
/// <param name="Id">The vocabulary identifier.</param>
/// <param name="Kind">What sort of thing the tag names.</param>
internal sealed record TagDefinition(string Id, TagKind? Kind);

/// <summary>One site-level link.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
internal sealed record SiteLinkEntry(SiteLinkType Type, Uri Url);

/// <summary>One declared section, shared by site and project configs.</summary>
/// <param name="Id">Stable within its owner.</param>
/// <param name="File">The locale-agnostic file name under <c>content/&lt;locale&gt;/</c>.</param>
internal sealed record SectionEntry(string Id, string File);
