namespace Folio.Domain.Model;

/// <summary>The site, resolved for one locale.</summary>
/// <param name="Url">The origin, and optional path prefix, of the live site.</param>
/// <param name="DefaultLocale">The locale content falls back to.</param>
/// <param name="Locales">Every locale the site publishes.</param>
/// <param name="Title">The site title.</param>
/// <param name="Tagline">The site tagline.</param>
/// <param name="Links">Site-level links, in declaration order.</param>
/// <param name="Sections">Site-level pages, in declaration order.</param>
/// <param name="Projects">Every project, in <c>projects.toml</c> order.</param>
public sealed record ResolvedSite(
    Uri Url,
    LocaleTag DefaultLocale,
    IReadOnlyList<LocaleTag> Locales,
    Localized<string>? Title,
    Localized<string>? Tagline,
    IReadOnlyList<ResolvedSiteLink> Links,
    IReadOnlyList<ResolvedSection> Sections,
    IReadOnlyList<ResolvedProject> Projects);

/// <summary>A site-level link with its localized label.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
/// <param name="Label">The label to render, if the vocabulary supplies one.</param>
public sealed record ResolvedSiteLink(SiteLinkType Type, Uri Url, Localized<string>? Label);
