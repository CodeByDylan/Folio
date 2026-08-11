using Folio.Api.Infrastructure;
using Folio.Domain.Model;

namespace Folio.Api.Features.Site;

/// <summary>A site-level link with its localized label.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
/// <param name="Label">The label to render.</param>
internal sealed record SiteLinkView(
    [property: WireEnum(typeof(SiteLinkType))] string Type,
    string Url,
    string? Label);

/// <summary>A page the site publishes. Its sections are served by <c>/pages/{slug}</c>.</summary>
/// <param name="Slug">Stable identity, and what the frontend builds its route from.</param>
/// <param name="Home">Whether this is the site's entry point.</param>
/// <param name="Nav">Whether the page belongs in the site navigation.</param>
/// <param name="NavLabel">The label to render in navigation.</param>
internal sealed record SitePageView(
    string Slug,
    bool Home,
    bool Nav,
    string? NavLabel);

/// <summary>Maps the resolved site onto its wire shapes.</summary>
internal static class SiteMapping
{
    /// <summary>Maps site links.</summary>
    /// <param name="site">The resolved site.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<SiteLinkView> Links(ResolvedSite site, ProvenanceScope scope) =>
    [
        .. site.Links.Select((link, index) => new SiteLinkView(
            Wire.Lower(link.Type),
            link.Url.ToString(),
            scope.Take(link.Label, $"/links/{index}/label"))),
    ];

    /// <summary>Maps interface strings, dropping any that resolved to nothing.</summary>
    /// <param name="site">The resolved site.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The strings, keyed as authored.</returns>
    public static IReadOnlyDictionary<string, string> Strings(
        ResolvedSite site,
        ProvenanceScope scope)
    {
        Dictionary<string, string> strings = new(StringComparer.Ordinal);

        foreach (string name in site.Strings.Keys.Order(StringComparer.Ordinal))
        {
            if (scope.Take(site.Strings[name], $"/strings/{name}") is { } value)
            {
                strings[name] = value;
            }
        }

        return strings;
    }

    /// <summary>Maps the page list, without the sections each page renders.</summary>
    /// <param name="site">The resolved site.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<SitePageView> Pages(ResolvedSite site, ProvenanceScope scope) =>
    [
        .. site.Pages.Select((page, index) => new SitePageView(
            page.Slug.Value,
            page.IsHome,
            page.InNav,
            scope.Take(page.NavLabel, $"/pages/{index}/navLabel"))),
    ];
}
