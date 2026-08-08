using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Projects.GetProject;

/// <summary>Reads one project in full.</summary>
/// <param name="View">The snapshot and locale to read.</param>
/// <param name="Slug">The project to read.</param>
internal sealed record Request(SnapshotView View, string Slug);

/// <summary>One project, including its section bodies.</summary>
/// <param name="RequestedLocale">The locale the caller asked for.</param>
/// <param name="Locale">The locale actually serving the response.</param>
/// <param name="Slug">The project's stable identity.</param>
/// <param name="Repo">The repository it was read from.</param>
/// <param name="PinnedSha">The commit its content was read from.</param>
/// <param name="Featured">Whether the site highlights it.</param>
/// <param name="Name">The project name.</param>
/// <param name="Tagline">The project tagline.</param>
/// <param name="Status">How active it is.</param>
/// <param name="Role">The part played in it.</param>
/// <param name="Started">When work began.</param>
/// <param name="Ended">When work concluded.</param>
/// <param name="Tags">Applied tags.</param>
/// <param name="Links">Project links.</param>
/// <param name="Relations">Declared and generated relations.</param>
/// <param name="Media">Named media.</param>
/// <param name="Sections">Authored prose.</param>
/// <param name="Metadata">The GitHub facts.</param>
/// <param name="Provenance">Fallbacks, keyed by RFC 6901 pointer.</param>
internal sealed record Response(
    string RequestedLocale,
    string Locale,
    string Slug,
    string Repo,
    string PinnedSha,
    bool Featured,
    string? Name,
    string? Tagline,
    string? Status,
    string? Role,
    string? Started,
    string? Ended,
    IReadOnlyList<TagView> Tags,
    IReadOnlyList<LinkView> Links,
    IReadOnlyList<RelationView> Relations,
    IReadOnlyList<MediaView> Media,
    IReadOnlyList<SectionView> Sections,
    MetadataView Metadata,
    IReadOnlyDictionary<string, ProvenanceEntry> Provenance);

internal sealed class Handler : IHandler<Request, Response>
{
    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Slug.TryParse(request.Slug, out _))
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.MalformedSlug(request.Slug));
        }

        ResolvedSite site = request.View.Snapshot.Localizations[request.View.Resolved];

        ResolvedProject? project = site.Projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug.Value, request.Slug, StringComparison.Ordinal));

        if (project is null)
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.UnknownProject(request.Slug));
        }

        Provenance provenance = new();
        ProvenanceScope scope = provenance.At(string.Empty);

        return Task.FromResult<Result<Response>>(new Response(
            request.View.Requested.Value,
            request.View.Resolved.Value,
            project.Slug.Value,
            project.Repo,
            project.PinnedSha,
            project.IsFeatured,
            scope.Take(project.Name, "/name"),
            scope.Take(project.Tagline, "/tagline"),
            project.Status is { } status ? Wire.Lower(status) : null,
            project.Role is { } role ? Wire.Lower(role) : null,
            project.Started,
            project.Ended,
            ProjectMapping.Tags(project, scope),
            ProjectMapping.Links(project, scope),
            ProjectMapping.Relations(project, scope),
            ProjectMapping.Media(project, scope),
            ProjectMapping.Sections(project.Sections, scope),
            ProjectMapping.Metadata(project.Metadata),
            provenance.Entries));
    }
}
