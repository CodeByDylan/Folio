using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Projects.ListProjects;

/// <summary>Lists every project in display order.</summary>
/// <param name="View">The snapshot and locale to read.</param>
internal sealed record Request(SnapshotView View);

/// <summary>The project index.</summary>
/// <param name="RequestedLocale">The locale the caller asked for.</param>
/// <param name="Locale">The locale actually serving the response.</param>
/// <param name="Projects">Project summaries, in <c>projects.toml</c> order.</param>
/// <param name="Provenance">Fallbacks, keyed by RFC 6901 pointer.</param>
internal sealed record Response(
    string RequestedLocale,
    string Locale,
    IReadOnlyList<ProjectSummary> Projects,
    IReadOnlyDictionary<string, ProvenanceEntry> Provenance);

/// <summary>One project without its section bodies.</summary>
/// <param name="Slug">The project's stable identity.</param>
/// <param name="Repo">The repository it was read from.</param>
/// <param name="Featured">Whether the site highlights it.</param>
/// <param name="Name">The project name.</param>
/// <param name="Tagline">The project tagline.</param>
/// <param name="Status">How active it is.</param>
/// <param name="Role">The part played in it.</param>
/// <param name="Started">When work began.</param>
/// <param name="Ended">When work concluded.</param>
/// <param name="Tags">Applied tags.</param>
/// <param name="Media">Named media.</param>
/// <param name="Metadata">The GitHub facts.</param>
internal sealed record ProjectSummary(
    string Slug,
    string Repo,
    bool Featured,
    string? Name,
    string? Tagline,
    [property: WireEnum(typeof(ProjectStatus))] string? Status,
    [property: WireEnum(typeof(ProjectRole))] string? Role,
    string? Started,
    string? Ended,
    IReadOnlyList<TagView> Tags,
    IReadOnlyList<MediaView> Media,
    MetadataView Metadata);

internal sealed class Handler : IHandler<Request, Response>
{
    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ResolvedSite site = request.View.Snapshot.Localizations[request.View.Resolved];
        Provenance provenance = new();

        List<ProjectSummary> projects = [];

        for (int index = 0; index < site.Projects.Count; index++)
        {
            ResolvedProject project = site.Projects[index];
            ProvenanceScope scope = provenance.At($"/projects/{index}");

            projects.Add(new ProjectSummary(
                project.Slug.Value,
                project.Repo,
                project.IsFeatured,
                scope.Take(project.Name, "/name"),
                scope.Take(project.Tagline, "/tagline"),
                project.Status is { } status ? Wire.Lower(status) : null,
                project.Role is { } role ? Wire.Lower(role) : null,
                project.Started,
                project.Ended,
                ProjectMapping.Tags(project, scope),
                ProjectMapping.Media(project, scope),
                ProjectMapping.Metadata(project.Metadata)));
        }

        return Task.FromResult<Result<Response>>(new Response(
            request.View.Requested.Value,
            request.View.Resolved.Value,
            projects,
            provenance.Entries));
    }
}
