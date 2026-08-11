using System.Text.Json.Serialization;
using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Pages.GetPage;

/// <summary>Reads one page and the sections it renders.</summary>
/// <param name="View">The snapshot and locale to read.</param>
/// <param name="Slug">The page to read.</param>
internal sealed record Request(SnapshotView View, string Slug);

/// <summary>One section of a page. The <c>type</c> discriminator says which shape it is.</summary>
/// <param name="Id">Stable within the site. The anchor and link target.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProseSectionView), "prose")]
[JsonDerivedType(typeof(HeroSectionView), "hero")]
internal abstract record PageSectionView(string Id);

/// <summary>A section of authored markdown.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Title">The section title.</param>
/// <param name="Body">The rewritten markdown body.</param>
/// <param name="Source">Where the body came from.</param>
internal sealed record ProseSectionView(
    string Id,
    string? Title,
    string? Body,
    [property: WireEnum(typeof(SectionSource))] string Source) : PageSectionView(Id);

/// <summary>The section a page opens with.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Headline">The claim the page opens with.</param>
/// <param name="Subheadline">The line beneath it.</param>
/// <param name="Actions">Calls to action, in declaration order.</param>
/// <param name="Media">Images by role, in role order.</param>
internal sealed record HeroSectionView(
    string Id,
    string? Headline,
    string? Subheadline,
    IReadOnlyList<HeroActionView> Actions,
    IReadOnlyList<HeroMediaView> Media) : PageSectionView(Id);

/// <summary>One call to action on a hero.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Url">An absolute URL, or a site-relative path when it points at this site.</param>
/// <param name="Label">The label to render.</param>
internal sealed record HeroActionView(string Id, string Url, string? Label);

/// <summary>One image on a hero.</summary>
/// <param name="Role">The role it fills, such as <c>image</c>.</param>
/// <param name="Url">An absolute URL pinned to the central repository's commit.</param>
/// <param name="Width">The intrinsic width, if measured.</param>
/// <param name="Height">The intrinsic height, if measured.</param>
/// <param name="Alt">The localized alt text.</param>
internal sealed record HeroMediaView(string Role, string Url, int? Width, int? Height, string? Alt);

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
        [.. page.Sections.Select((section, index) => Section(section, index, scope))];

    private static PageSectionView Section(ResolvedSection section, int index, ProvenanceScope scope) =>
        section.Hero is { } hero
            ? new HeroSectionView(
                section.Id,
                scope.Take(hero.Headline, $"/sections/{index}/headline"),
                scope.Take(hero.Subheadline, $"/sections/{index}/subheadline"),
                [
                    .. hero.Actions.Select((action, position) => new HeroActionView(
                        action.Id,
                        action.Url,
                        scope.Take(action.Label, $"/sections/{index}/actions/{position}/label"))),
                ],
                [
                    .. hero.Media.Select((media, position) => new HeroMediaView(
                        media.Role,
                        media.Url.ToString(),
                        media.Width,
                        media.Height,
                        scope.Take(media.Alt, $"/sections/{index}/media/{position}/alt"))),
                ])
            : new ProseSectionView(
                section.Id,
                scope.Take(section.Title, $"/sections/{index}/title"),
                scope.Take(section.Body, $"/sections/{index}/body"),
                Wire.Lower(section.Source));
}
