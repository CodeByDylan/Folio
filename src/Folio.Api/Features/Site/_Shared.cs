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

/// <summary>A site-level page.</summary>
/// <param name="Id">Stable identifier and route fragment.</param>
/// <param name="Title">The page title.</param>
/// <param name="Body">The rewritten markdown body.</param>
/// <param name="Source">Where the body came from.</param>
internal sealed record SitePageView(
    string Id,
    string? Title,
    string? Body,
    [property: WireEnum(typeof(SectionSource))] string Source);

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

    /// <summary>Maps site pages.</summary>
    /// <param name="site">The resolved site.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<SitePageView> Sections(ResolvedSite site, ProvenanceScope scope) =>
    [
        .. site.Sections.Select((section, index) => new SitePageView(
            section.Id,
            scope.Take(section.Title, $"/sections/{index}/title"),
            scope.Take(section.Body, $"/sections/{index}/body"),
            Wire.Lower(section.Source))),
    ];
}
