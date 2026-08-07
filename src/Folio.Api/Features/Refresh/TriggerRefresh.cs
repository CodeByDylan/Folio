using System.Reflection;
using Folio.Api.Infrastructure;
using Folio.Api.Options;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Resolution;
using Folio.Ingestion;
using Folio.Ingestion.GitHub;
using Folio.Ingestion.Snapshots;
using Loom.Handlers;
using Loom.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Folio.Api.Features.Refresh.TriggerRefresh;

/// <summary>Rebuilds the portfolio.</summary>
internal sealed record Request;

/// <summary>What a rebuild produced.</summary>
/// <param name="SnapshotId">The id of the snapshot now being served.</param>
/// <param name="BuiltAt">When it finished building.</param>
/// <param name="Projects">How many projects it contains.</param>
/// <param name="Diagnostics">How many diagnostics it produced.</param>
internal sealed record Response(string SnapshotId, DateTimeOffset BuiltAt, int Projects, int Diagnostics);

internal sealed class Handler(
    IGitHubContentSource source,
    ISnapshotStore store,
    ISnapshotProvider snapshots,
    IRefreshReporter reporter,
    RefreshGate gate,
    FolioMetrics metrics,
    IOptionsMonitor<RefreshOptions> options,
    TimeProvider clock,
    ILogger<Handler> logger) : IHandler<Request, Response>
{
    private static readonly string Version =
        typeof(Handler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken) =>
        gate.RunAsync(cancellationToken, GuardedAsync);

    // The timer and the endpoint share this, so neither can rebuild without a limit.
    private async Task<Result<Response>> GuardedAsync(CancellationToken cancellationToken)
    {
        TimeSpan limit = options.CurrentValue.Timeout;
        long started = clock.GetTimestamp();

        using CancellationTokenSource elapsed = new(limit, clock);
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, elapsed.Token);

        try
        {
            return await RunAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Abandon(
                FolioIngestionErrors.Transient(new TimeoutException($"The refresh exceeded {limit}.")),
                started);
        }
    }

    private async Task<Result<Response>> RunAsync(CancellationToken cancellationToken)
    {
        long started = clock.GetTimestamp();
        StoredInputs? previous = await store.ReadAsync(cancellationToken);
        Result<FetchResult> fetched = await source.FetchAsync(previous, cancellationToken);

        if (fetched.IsFailure)
        {
            // A boot with GitHub unreachable still serves: stale content beats no content.
            if (snapshots.Current is null && previous is not null)
            {
                PublishStored(previous);
            }

            return Abandon(fetched.Error, started);
        }

        Result<Snapshot> resolved = new PortfolioResolver().Resolve(
            new CentralInput(
                fetched.Value.Inputs.CentralRepo,
                fetched.Value.Inputs.CentralSha,
                fetched.Value.Inputs.Central),
            fetched.Value.Inputs.Repos,
            Version,
            clock.GetUtcNow(),
            fetched.Value.Diagnostics);

        if (resolved.IsFailure)
        {
            return Fatal(resolved.Error, started);
        }

        snapshots.Publish(resolved.Value);

        try
        {
            await store.WriteAsync(fetched.Value.Inputs, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The snapshot is already published, so a store that breaks its contract must not undo that.
            RefreshLog.StoreWriteFailed(logger, exception);
        }

        reporter.Record(new RefreshReport(clock.GetUtcNow(), RefreshOutcome.Succeeded, []));
        metrics.Refreshed(RefreshOutcome.Succeeded, clock.GetElapsedTime(started));
        metrics.GitHubCalls(fetched.Value.Requests);
        metrics.BudgetRemaining(fetched.Value.BudgetRemaining);

        ResolvedSite site = resolved.Value.Localizations[resolved.Value.DefaultLocale];
        RefreshLog.Published(logger, resolved.Value.Id, site.Projects.Count, resolved.Value.Diagnostics.Count);

        return new Response(
            resolved.Value.Id,
            resolved.Value.BuiltAt,
            site.Projects.Count,
            resolved.Value.Diagnostics.Count);
    }

    private void PublishStored(StoredInputs previous)
    {
        Result<Snapshot> resolved = new PortfolioResolver().Resolve(
            new CentralInput(previous.CentralRepo, previous.CentralSha, previous.Central),
            previous.Repos,
            Version,
            // Stamp the capture time, not now, so BuiltAt / Last-Modified / snapshot age tell the truth.
            previous.CapturedAt);

        if (resolved.IsSuccess)
        {
            snapshots.Publish(resolved.Value);
            RefreshLog.ServedFromStore(logger, resolved.Value.Id, previous.CapturedAt);
        }
    }

    private Error Abandon(Error error, long started)
    {
        DiagnosticSink sink = new();

        bool configuration = error.Code
            is FolioIngestionErrors.CentralUnreadableCode
            or FolioIngestionErrors.CentralUnparseableCode;

        string code = error.Code switch
        {
            FolioIngestionErrors.RateLimitInsufficientCode => DiagnosticCodes.RefreshRateLimitInsufficient,
            FolioIngestionErrors.CentralUnreadableCode => DiagnosticCodes.CentralMissing,
            FolioIngestionErrors.CentralUnparseableCode => DiagnosticCodes.CentralUnparseable,
            _ => DiagnosticCodes.RefreshAbandoned,
        };

        RefreshOutcome outcome = configuration ? RefreshOutcome.FailedFatal : RefreshOutcome.AbandonedTransient;

        sink.Error(code, error.Message);
        reporter.Record(new RefreshReport(clock.GetUtcNow(), outcome, sink.Diagnostics));
        metrics.Refreshed(outcome, clock.GetElapsedTime(started));

        // The one abandon that knows the figure is the one where the figure is the reason.
        if (error is Error<int> budget)
        {
            metrics.BudgetRemaining(budget.Metadata);
        }

        RefreshLog.Abandoned(logger, error.Message);
        return error;
    }

    private Error Fatal(Error error, long started)
    {
        IReadOnlyList<Diagnostic> found = error is Error<IReadOnlyList<Diagnostic>> detailed
            ? detailed.Metadata
            : [];

        reporter.Record(new RefreshReport(clock.GetUtcNow(), RefreshOutcome.FailedFatal, found));
        metrics.Refreshed(RefreshOutcome.FailedFatal, clock.GetElapsedTime(started));

        RefreshLog.Abandoned(logger, error.Message);
        return error;
    }
}

internal static partial class RefreshLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh abandoned; the previous snapshot keeps serving. {Reason}")]
    public static partial void Abandoned(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published snapshot {SnapshotId}: {Projects} projects, {Diagnostics} diagnostics.")]
    public static partial void Published(ILogger logger, string snapshotId, int projects, int diagnostics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The snapshot was published but its inputs could not be stored.")]
    public static partial void StoreWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Published snapshot {SnapshotId} from inputs stored {CapturedAt}; GitHub was unreachable.")]
    public static partial void ServedFromStore(ILogger logger, string snapshotId, DateTimeOffset capturedAt);
}
