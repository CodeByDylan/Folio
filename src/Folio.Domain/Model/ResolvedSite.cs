namespace Folio.Domain.Model;

/// <summary>The site, resolved for one locale.</summary>
/// <param name="Url">The origin, and optional path prefix, of the live site.</param>
/// <param name="DefaultLocale">The locale content falls back to.</param>
/// <param name="Locales">Every locale the site publishes.</param>
/// <param name="Title">The site title.</param>
/// <param name="Tagline">The site tagline.</param>
/// <param name="Links">Site-level links, in declaration order.</param>
/// <param name="Pages">The site's pages, in declaration order.</param>
/// <param name="Projects">Every project, in <c>projects.toml</c> order.</param>
/// <param name="Strings">Interface strings the site declares, by key without the <c>ui.</c> prefix.</param>
public sealed record ResolvedSite(
    Uri Url,
    LocaleTag DefaultLocale,
    IReadOnlyList<LocaleTag> Locales,
    Localized<string>? Title,
    Localized<string>? Tagline,
    IReadOnlyList<ResolvedSiteLink> Links,
    IReadOnlyList<ResolvedPage> Pages,
    IReadOnlyList<ResolvedProject> Projects,
    IReadOnlyDictionary<string, Localized<string>> Strings);

/// <summary>One page of the site, resolved for one locale.</summary>
/// <param name="Slug">Stable identity, and what the frontend builds its route from.</param>
/// <param name="IsHome">Whether this is the site's entry point.</param>
/// <param name="InNav">Whether the page belongs in the site navigation.</param>
/// <param name="NavLabel">The label to render in navigation, if the vocabulary supplies one.</param>
/// <param name="Sections">The sections it renders, in declaration order.</param>
public sealed record ResolvedPage(
    Slug Slug,
    bool IsHome,
    bool InNav,
    Localized<string>? NavLabel,
    IReadOnlyList<ResolvedSection> Sections);

/// <summary>A site-level link with its localized label.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
/// <param name="Label">The label to render, if the vocabulary supplies one.</param>
public sealed record ResolvedSiteLink(SiteLinkType Type, Uri Url, Localized<string>? Label);
