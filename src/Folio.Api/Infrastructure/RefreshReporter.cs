using Folio.Domain.Diagnostics;

namespace Folio.Api.Infrastructure;

/// <summary>How the most recent rebuild ended.</summary>
internal enum RefreshOutcome
{
    /// <summary>A new snapshot was published.</summary>
    Succeeded,

    /// <summary>A fault that may pass ended the attempt; the previous snapshot keeps serving.</summary>
    AbandonedTransient,

    /// <summary>The central configuration could not be read; the previous snapshot keeps serving.</summary>
    FailedFatal,
}

/// <summary>What the most recent rebuild attempt produced.</summary>
/// <param name="AttemptedAt">When the attempt ran.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Diagnostics">What it found, empty when it succeeded.</param>
internal sealed record RefreshReport(
    DateTimeOffset AttemptedAt,
    RefreshOutcome Outcome,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Holds the outcome of the last rebuild attempt, which may be newer than the snapshot.</summary>
internal interface IRefreshReporter
{
    /// <summary>Gets the last attempt, or <see langword="null" /> before one has run.</summary>
    RefreshReport? Last { get; }

    /// <summary>Records an attempt.</summary>
    /// <param name="report">What the attempt produced.</param>
    void Record(RefreshReport report);
}

/// <inheritdoc />
internal sealed class RefreshReporter : IRefreshReporter
{
    private RefreshReport? _last;

    /// <inheritdoc />
    public RefreshReport? Last => Volatile.Read(ref _last);

    /// <inheritdoc />
    public void Record(RefreshReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _ = Interlocked.Exchange(ref _last, report);
    }
}
