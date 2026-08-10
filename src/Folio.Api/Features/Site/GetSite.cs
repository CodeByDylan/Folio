using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Site.GetSite;

/// <summary>Reads the site-level facts and pages.</summary>
/// <param name="View">The snapshot and locale to read.</param>
internal sealed record Request(SnapshotView View);

/// <summary>The site.</summary>
/// <param name="RequestedLocale">The locale the caller asked for.</param>
/// <param name="Locale">The locale actually serving the response.</param>
/// <param name="Url">The origin, and optional path prefix, of the live site.</param>
/// <param name="DefaultLocale">The locale content falls back to.</param>
/// <param name="Locales">Every locale the site publishes.</param>
/// <param name="Title">The site title.</param>
/// <param name="Tagline">The site tagline.</param>
/// <param name="Links">Site-level links.</param>
/// <param name="Sections">Site-level pages, with their bodies.</param>
/// <param name="Strings">Interface strings, keyed as authored without the <c>ui.</c> prefix.</param>
/// <param name="Provenance">Fallbacks, keyed by RFC 6901 pointer.</param>
internal sealed record Response(
    string RequestedLocale,
    string Locale,
    string Url,
    string DefaultLocale,
    IReadOnlyList<string> Locales,
    string? Title,
    string? Tagline,
    IReadOnlyList<SiteLinkView> Links,
    IReadOnlyList<SitePageView> Sections,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, ProvenanceEntry> Provenance);

internal sealed class Handler : IHandler<Request, Response>
{
    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ResolvedSite site = request.View.Snapshot.Localizations[request.View.Resolved];
        Provenance provenance = new();
        ProvenanceScope scope = provenance.At(string.Empty);

        return Task.FromResult<Result<Response>>(new Response(
            request.View.Requested.Value,
            request.View.Resolved.Value,
            site.Url.ToString(),
            site.DefaultLocale.Value,
            [.. site.Locales.Select(locale => locale.Value)],
            scope.Take(site.Title, "/title"),
            scope.Take(site.Tagline, "/tagline"),
            SiteMapping.Links(site, scope),
            SiteMapping.Sections(site, scope),
            SiteMapping.Strings(site, scope),
            provenance.Entries));
    }
}
