using System.Diagnostics.Metrics;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Api.Infrastructure;

/// <summary>The instruments Folio publishes.</summary>
internal sealed class FolioMetrics
{
    private readonly Histogram<double> _refreshDuration;
    private readonly Counter<long> _refreshOutcomes;
    private readonly Counter<long> _githubCalls;

    private int _budgetRemaining = -1;

    /// <summary>Creates the instruments and the gauges that observe current state.</summary>
    /// <param name="factory">The meter factory.</param>
    /// <param name="snapshots">The snapshot being served.</param>
    /// <param name="clock">The clock snapshot age is measured against.</param>
    public FolioMetrics(IMeterFactory factory, SnapshotProvider snapshots, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Meter meter = factory.Create(Telemetry.SourceName);

        _refreshDuration = meter.CreateHistogram<double>(
            "folio.refresh.duration", "s", "How long a rebuild took.");

        _refreshOutcomes = meter.CreateCounter<long>(
            "folio.refresh.outcome", "{rebuild}", "Rebuilds by how they ended.");

        _githubCalls = meter.CreateCounter<long>(
            "folio.github.calls", "{request}", "GitHub requests issued by a rebuild.");
        _ = meter.CreateObservableGauge(
            "folio.snapshot.age",
            () => snapshots.Current is { } snapshot
                ? (clock.GetUtcNow() - snapshot.BuiltAt).TotalSeconds
                : -1,
            "s",
            "How long ago the snapshot being served was built.");

        _ = meter.CreateObservableGauge(
            "folio.github.budget_remaining",
            () => Volatile.Read(ref _budgetRemaining),
            "{request}",
            "GitHub requests left in the current rate-limit window.");

        _ = meter.CreateObservableGauge(
            "folio.diagnostics.count",
            () => snapshots.Current is { } snapshot
                ? [.. snapshot.Diagnostics
                    .GroupBy(diagnostic => diagnostic.Severity)
                    .Select(group => new Measurement<long>(
                        group.LongCount(),
                        new KeyValuePair<string, object?>("severity", Wire.Lower(group.Key))))]
                : Array.Empty<Measurement<long>>(),
            "{diagnostic}",
            "Diagnostics in the snapshot being served, by severity.");
    }

    /// <summary>Records a completed rebuild.</summary>
    /// <param name="outcome">How it ended.</param>
    /// <param name="duration">How long it took.</param>
    public void Refreshed(RefreshOutcome outcome, TimeSpan duration)
    {
        _refreshDuration.Record(duration.TotalSeconds);
        _refreshOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", Wire.Hyphenate(outcome.ToString())));
    }

    /// <summary>Records GitHub requests issued by a rebuild.</summary>
    /// <param name="count">How many were issued.</param>
    public void GitHubCalls(int count) => _githubCalls.Add(count);

    /// <summary>Records the API budget left after a rebuild.</summary>
    /// <param name="remaining">Requests left in the current window.</param>
    public void BudgetRemaining(int remaining) => Volatile.Write(ref _budgetRemaining, remaining);
}
