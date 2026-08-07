namespace Folio.Ingestion.Snapshots;

/// <summary>Persists the raw inputs the last successful build ran over.</summary>
public interface ISnapshotStore
{
    /// <summary>Reads previously stored inputs.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored inputs, or <see langword="null" /> if none have been stored.</returns>
    Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the stored inputs, best-effort.</summary>
    /// <remarks>The store is an optimization, so a storage failure is logged, not thrown.</remarks>
    /// <param name="inputs">The inputs the most recent successful build used.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completing when the write is durable.</returns>
    Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken);
}
