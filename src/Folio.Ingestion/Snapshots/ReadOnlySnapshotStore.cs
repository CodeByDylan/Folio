namespace Folio.Ingestion.Snapshots;

/// <summary>
/// Reads through to another store and drops every write. A replayed capture is an input, so a build
/// made from it must not overwrite the recording it came from.
/// </summary>
/// <param name="inner">The store holding the capture.</param>
public sealed class ReadOnlySnapshotStore(ISnapshotStore inner) : ISnapshotStore
{
    /// <inheritdoc />
    public Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken) =>
        inner.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken) => Task.CompletedTask;
}
