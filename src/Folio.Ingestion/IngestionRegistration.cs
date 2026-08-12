using Folio.Ingestion.GitHub;
using Folio.Ingestion.Media;
using Folio.Ingestion.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using Octokit.Internal;
using StackExchange.Redis;

namespace Folio.Ingestion;

/// <summary>Where the last successful build's inputs are kept.</summary>
public enum SnapshotStoreKind
{
    /// <summary>A local file.</summary>
    File,

    /// <summary>Redis.</summary>
    Redis,
}

/// <summary>Where a build's inputs come from.</summary>
public enum ContentSourceKind
{
    /// <summary>Fetched from GitHub.</summary>
    GitHub,

    /// <summary>Replayed from a recorded capture, with local working trees laid over it.</summary>
    Replay,
}

/// <summary>Everything the ingestion layer needs to be composed.</summary>
/// <param name="Token">The GitHub token.</param>
/// <param name="Fetch">How a fetch behaves.</param>
/// <param name="Store">Which snapshot store to use.</param>
/// <param name="FilePath">Where the file store writes.</param>
/// <param name="RedisConnectionString">How to reach Redis.</param>
/// <param name="Content">Where inputs come from.</param>
/// <param name="Overlay">The working trees laid over a replayed capture.</param>
public sealed record IngestionSettings(
    string? Token,
    FetchSettings Fetch,
    SnapshotStoreKind Store,
    string FilePath,
    string? RedisConnectionString,
    ContentSourceKind Content = ContentSourceKind.GitHub,
    OverlaySettings? Overlay = null);

/// <summary>Composes the ingestion layer, keeping its dependencies out of the host.</summary>
public static class IngestionRegistration
{
    private const string RedisKey = "folio:inputs";

    /// <summary>Registers the GitHub client, the media probe and the snapshot store.</summary>
    /// <param name="services">The collection to register into.</param>
    /// <param name="settings">Resolves settings from the built container, so validated options are used.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFolioIngestion(
        this IServiceCollection services,
        Func<IServiceProvider, IngestionSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        _ = services.AddSingleton<EtagCache>();

        _ = services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            ConfigurationOptions redis = ConfigurationOptions.Parse(settings(provider).RedisConnectionString!);

            // The store is best-effort, so an unreachable Redis must not fail service resolution.
            redis.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(redis);
        });

        _ = services.AddSingleton<IGitHubClient>(provider =>
        {
            IHttpClient http = new ConditionalHttpClient(
                new HttpClientAdapter(HttpMessageHandlerFactory.CreateDefault),
                provider.GetRequiredService<EtagCache>());

            return new GitHubClient(new Connection(new ProductHeaderValue("folio"), http))
            {
                Credentials = new Credentials(settings(provider).Token
                    ?? throw new InvalidOperationException("GitHub:Token is required to reach GitHub.")),
            };
        });

        // Resolved per use so the singleton content source does not pin one handler for the process.
        _ = services.AddHttpClient<MediaProbe>();
        _ = services.AddSingleton<IMediaProbe>(provider =>
            new PooledMediaProbe(provider.GetRequiredService<IHttpClientFactory>()));

        _ = services.AddSingleton(provider => settings(provider).Fetch);

        _ = services.AddSingleton<IGitHubContentSource>(provider =>
            settings(provider) is { Content: ContentSourceKind.Replay, Overlay: { } overlay }
                ? new ReplayContentSource(overlay, provider.GetRequiredService<ILogger<ReplayContentSource>>())
                : ActivatorUtilities.CreateInstance<GitHubContentSource>(provider));

        _ = services.AddSingleton<ISnapshotStore>(provider =>
        {
            ISnapshotStore store = settings(provider) is { Store: SnapshotStoreKind.Redis }
                ? new RedisSnapshotStore(
                    provider.GetRequiredService<IConnectionMultiplexer>(),
                    RedisKey,
                    provider.GetRequiredService<ILogger<RedisSnapshotStore>>())
                : new FileSnapshotStore(
                    settings(provider).FilePath,
                    provider.GetRequiredService<ILogger<FileSnapshotStore>>());

            // A replayed build must not write over the capture it was replayed from.
            return settings(provider).Content is ContentSourceKind.Replay
                ? new ReadOnlySnapshotStore(store)
                : store;
        });

        return services;
    }
}
