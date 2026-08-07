using Folio.Ingestion.Snapshots;
using Loom.Results;

namespace Folio.Ingestion.GitHub;

/// <summary>Assembles the input set for a build from GitHub.</summary>
public interface IGitHubContentSource
{
    /// <summary>Fetches everything a build needs.</summary>
    /// <param name="previous">The last successful build's inputs, used for SHA and ETag revalidation.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The assembled inputs with any content faults, or a transient failure.</returns>
    Task<Result<FetchResult>> FetchAsync(StoredInputs? previous, CancellationToken cancellationToken);
}
