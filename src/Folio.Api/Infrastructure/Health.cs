using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Folio.Api.Infrastructure;

/// <summary>Liveness and readiness wiring.</summary>
internal static class Health
{
    private const string ReadyTag = "ready";

    /// <summary>Registers the readiness check.</summary>
    /// <param name="services">The collection to register into.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFolioHealthChecks(this IServiceCollection services)
    {
        _ = services.AddHealthChecks().AddCheck<SnapshotHealthCheck>("snapshot", tags: [ReadyTag]);
        return services;
    }

    /// <summary>Maps the liveness and readiness endpoints.</summary>
    /// <param name="app">The application to map into.</param>
    /// <returns>The same application.</returns>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Outside the versioned contract, so the caller-facing rate limit must not answer for them.
        _ = app.MapHealthChecks("/alive", new() { Predicate = _ => false }).DisableRateLimiting();
        _ = app.MapHealthChecks("/ready", new() { Predicate = check => check.Tags.Contains(ReadyTag) })
            .DisableRateLimiting();

        return app;
    }
}

/// <summary>Reports unready until the first snapshot has been built.</summary>
internal sealed class SnapshotHealthCheck(ISnapshotProvider snapshots) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshots.Current is null
            ? HealthCheckResult.Unhealthy("No snapshot has been built yet.")
            : HealthCheckResult.Healthy());
}
