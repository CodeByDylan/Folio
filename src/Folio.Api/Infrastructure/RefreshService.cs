using Folio.Api.Options;
using Folio.Domain.Diagnostics;
using Loom.Handlers;
using Loom.Results;
using Microsoft.Extensions.Options;
using Slice = Folio.Api.Features.Refresh.TriggerRefresh;

namespace Folio.Api.Infrastructure;

/// <summary>Rebuilds the portfolio on a timer, through the same handler the endpoint uses.</summary>
internal sealed class RefreshService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<RefreshOptions> options,
    IRefreshReporter reporter,
    FolioMetrics metrics,
    TimeProvider clock,
    ILogger<RefreshService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.CurrentValue.Interval, clock);

        do
        {
            await RunAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        long started = clock.GetTimestamp();

        try
        {
            _ = await scope.ServiceProvider
                .GetRequiredService<IHandler<Slice.Request, Slice.Response>>()
                .HandleAsync(new Slice.Request(), stoppingToken);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The handler owns the timeout; this is a rebuild joined from a caller that went away.
            RefreshServiceLog.Cancelled(logger);
            Abandon("The refresh it joined was cancelled.", started);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RefreshServiceLog.Faulted(logger, exception);
            Abandon($"The refresh failed unexpectedly: {exception.Message}", started);
        }
    }

    // The handler reports its own failures; these are the ones that escape it.
    private void Abandon(string reason, long started)
    {
        DiagnosticSink sink = new();
        sink.Error(DiagnosticCodes.RefreshAbandoned, reason);

        reporter.Record(new RefreshReport(clock.GetUtcNow(), RefreshOutcome.AbandonedTransient, sink.Diagnostics));
        metrics.Refreshed(RefreshOutcome.AbandonedTransient, clock.GetElapsedTime(started));
    }
}

internal static partial class RefreshServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh was cancelled before it finished.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refresh faulted; the loop continues on the next tick.")]
    public static partial void Faulted(ILogger logger, Exception exception);
}
