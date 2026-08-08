using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Configuration;

/// <summary>One repository the central config asks for.</summary>
/// <param name="Repo">The repository, as <c>owner/name</c>.</param>
/// <param name="Path">The subdirectory containing <c>.folio</c>, empty at the repository root.</param>
/// <param name="Ref">The branch, tag or SHA to read from, or <see langword="null" /> for the default branch.</param>
/// <param name="UseReadme">Whether the README needs fetching as well.</param>
public sealed record ProjectReference(string Repo, string Path, string? Ref, bool UseReadme);

/// <summary>Reads the fetch list out of a central <c>.folio</c>, so ingestion knows what to collect.</summary>
public static class ProjectListReader
{
    /// <summary>Reads <c>projects.toml</c>.</summary>
    /// <param name="central">The central <c>.folio</c> contents.</param>
    /// <param name="references">The repositories to fetch, in declaration order.</param>
    /// <returns><see langword="false" /> if the central config could not be read.</returns>
    public static bool TryRead(FileSet central, out IReadOnlyList<ProjectReference> references)
    {
        ArgumentNullException.ThrowIfNull(central);

        references = [];

        if (!new CentralConfigParser().TryParse(central, new DiagnosticSink(), out CentralConfig config))
        {
            return false;
        }

        references =
        [
            .. config.Projects.Select(entry =>
                new ProjectReference(entry.Repo, entry.Path, entry.Ref, entry.UseReadme)),
        ];

        return true;
    }
}
