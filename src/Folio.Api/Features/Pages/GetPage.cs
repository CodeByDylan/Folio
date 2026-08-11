using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Pages.GetPage;

/// <summary>Reads one page and the sections it renders.</summary>
/// <param name="View">The snapshot and locale to read.</param>
/// <param name="Slug">The page to read.</param>
internal sealed record Request(SnapshotView View, string Slug);

/// <summary>One section of a page. <c>Type</c> discriminates what renders it.</summary>
/// <param name="Id">Stable within the site. The anchor and link target.</param>
/// <param name="Type">What the section holds.</param>
/// <param name="Title">The section title.</param>
/// <param name="Body">The rewritten markdown body.</param>
/// <param name="Source">Where the body came from.</param>
internal sealed record PageSectionView(
    string Id,
    [property: WireEnum(typeof(SectionType))] string Type,
    string? Title,
    string? Body,
    [property: WireEnum(typeof(SectionSource))] string Source);

/// <summary>One page, including its section bodies.</summary>
/// <param name="RequestedLocale">The locale the caller asked for.</param>
/// <param name="Locale">The locale actually serving the response.</param>
/// <param name="Slug">The page's stable identity.</param>
/// <param name="Home">Whether this is the site's entry point, so a frontend can canonicalize its route.</param>
/// <param name="NavLabel">The label to render, in navigation and as the page heading.</param>
/// <param name="Sections">The sections it renders, in declaration order.</param>
/// <param name="Provenance">Fallbacks, keyed by RFC 6901 pointer.</param>
internal sealed record Response(
    string RequestedLocale,
    string Locale,
    string Slug,
    bool Home,
    string? NavLabel,
    IReadOnlyList<PageSectionView> Sections,
    IReadOnlyDictionary<string, ProvenanceEntry> Provenance);

internal sealed class Handler : IHandler<Request, Response>
{
    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Slug.TryParse(request.Slug, out Slug slug))
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.MalformedSlug(request.Slug));
        }

        ResolvedSite site = request.View.Snapshot.Localizations[request.View.Resolved];

        ResolvedPage? page = site.Pages.FirstOrDefault(candidate => candidate.Slug == slug);

        if (page is null)
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.UnknownPage(request.Slug));
        }

        Provenance provenance = new();
        ProvenanceScope scope = provenance.At(string.Empty);

        return Task.FromResult<Result<Response>>(new Response(
            request.View.Requested.Value,
            request.View.Resolved.Value,
            page.Slug.Value,
            page.IsHome,
            scope.Take(page.NavLabel, "/navLabel"),
            Sections(page, scope),
            provenance.Entries));
    }

    private static IReadOnlyList<PageSectionView> Sections(ResolvedPage page, ProvenanceScope scope) =>
    [
        .. page.Sections.Select((section, index) => new PageSectionView(
            section.Id,
            Wire.Lower(section.Type),
            scope.Take(section.Title, $"/sections/{index}/title"),
            scope.Take(section.Body, $"/sections/{index}/body"),
            Wire.Lower(section.Source))),
    ];
}
