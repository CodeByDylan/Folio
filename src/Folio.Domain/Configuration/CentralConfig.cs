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
/// <param name="Sections">Site-level sections, in declaration order.</param>
/// <param name="Pages">The pages composed from those sections, in declaration order.</param>
internal sealed record SiteConfig(
    Uri Url,
    LocaleTag DefaultLocale,
    IReadOnlyList<LocaleTag> Locales,
    string Owner,
    IReadOnlyList<SiteLinkEntry> Links,
    IReadOnlyList<SectionEntry> Sections,
    IReadOnlyList<PageEntry> Pages);

/// <summary>One entry in <c>[[site.pages]]</c>.</summary>
/// <param name="Slug">Stable identity, and what the frontend builds its route from.</param>
/// <param name="IsHome">Whether this is the site's entry point.</param>
/// <param name="InNav">Whether the page belongs in the site navigation.</param>
/// <param name="Sections">The sections it renders, in declaration order.</param>
internal sealed record PageEntry(Slug Slug, bool IsHome, bool InNav, IReadOnlyList<string> Sections);

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
/// <param name="Type">What the section holds. Project sections are always prose.</param>
/// <param name="File">The locale-agnostic file name under <c>content/&lt;locale&gt;/</c>, for prose only.</param>
/// <param name="Hero">The data read from <c>sections/&lt;id&gt;.toml</c>, for a hero only.</param>
/// <param name="Skills">The data read from <c>sections/&lt;id&gt;.toml</c>, for a skills section only.</param>
/// <param name="Questions">The entry ids read from <c>sections/&lt;id&gt;.toml</c>, for a Q&amp;A only.</param>
/// <param name="Projects">The selection read from <c>sections/&lt;id&gt;.toml</c>, for a projects section only.</param>
internal sealed record SectionEntry(
    string Id,
    SectionType Type,
    string? File,
    HeroConfig? Hero = null,
    SkillsConfig? Skills = null,
    IReadOnlyList<string>? Questions = null,
    ProjectsConfig? Projects = null);

/// <summary><c>sections/&lt;id&gt;.toml</c> for a projects section, parsed.</summary>
/// <param name="Featured">Whether to show only the projects the site highlights.</param>
/// <param name="Limit">How many to show at most, or <see langword="null" /> for all of them.</param>
internal sealed record ProjectsConfig(bool Featured, int? Limit);

/// <summary><c>sections/&lt;id&gt;.toml</c> for a skills section, parsed.</summary>
/// <param name="Categories">The categories, in declaration order.</param>
internal sealed record SkillsConfig(IReadOnlyList<SkillCategoryEntry> Categories);

/// <summary>One category of skills.</summary>
/// <param name="Id">Stable within the section; the suffix of its label key.</param>
/// <param name="Skills">The skills it holds, in declaration order.</param>
internal sealed record SkillCategoryEntry(string Id, IReadOnlyList<SkillEntry> Skills);

/// <summary>One rated skill.</summary>
/// <param name="Id">Stable within the section; the suffix of its label key.</param>
/// <param name="Level">How well it is known.</param>
internal sealed record SkillEntry(string Id, SkillLevel Level);

/// <summary><c>sections/&lt;id&gt;.toml</c> for a hero section, parsed.</summary>
/// <param name="Actions">Calls to action, in declaration order.</param>
/// <param name="Media">Image references keyed by role, such as <c>image</c>.</param>
internal sealed record HeroConfig(
    IReadOnlyList<HeroActionEntry> Actions,
    IReadOnlyDictionary<string, string> Media);

/// <summary>One call to action on a hero.</summary>
/// <param name="Id">Stable within the section; the suffix of its label key.</param>
/// <param name="Url">Where it points, absolute as authored.</param>
internal sealed record HeroActionEntry(string Id, Uri Url);
