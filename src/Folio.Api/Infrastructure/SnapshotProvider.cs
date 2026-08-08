using Folio.Domain.Model;

namespace Folio.Api.Infrastructure;

/// <summary>Holds the portfolio currently being served.</summary>
internal interface ISnapshotProvider
{
    /// <summary>Gets the current snapshot, or <see langword="null" /> before the first build completes.</summary>
    Snapshot? Current { get; }

    /// <summary>Makes a newly built snapshot the one served.</summary>
    /// <param name="snapshot">The snapshot to publish.</param>
    void Publish(Snapshot snapshot);
}

/// <inheritdoc />
internal sealed class SnapshotProvider : ISnapshotProvider
{
    private Snapshot? _current;

    /// <inheritdoc />
    public Snapshot? Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public void Publish(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Interlocked.Exchange(ref _current, snapshot);
    }
}
