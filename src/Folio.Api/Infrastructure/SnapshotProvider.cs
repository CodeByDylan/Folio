using Folio.Domain.Model;

namespace Folio.Api.Infrastructure;

/// <summary>Holds the portfolio currently being served.</summary>
internal sealed class SnapshotProvider
{
    private Snapshot? _current;

    /// <summary>Gets the current snapshot, or <see langword="null" /> before the first build completes.</summary>
    public Snapshot? Current => Volatile.Read(ref _current);

    /// <summary>Makes a newly built snapshot the one served.</summary>
    /// <param name="snapshot">The snapshot to publish.</param>
    public void Publish(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Interlocked.Exchange(ref _current, snapshot);
    }
}
