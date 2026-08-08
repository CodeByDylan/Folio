using System.Diagnostics;
using System.Text;
using Folio.Domain.Model;
using Folio.Ingestion.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Folio.Ingestion.Tests;

/// <summary>Skips rather than fails where no container runtime is available.</summary>
internal sealed class RequiresDockerAttribute() : SkipAttribute("Docker is unavailable.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(!Docker.Available);
}

internal static class Docker
{
    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            using Process? docker = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            return docker is not null && docker.WaitForExit(10_000) && docker.ExitCode == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    });

    public static bool Available => Probe.Value;
}

/// <summary>The Redis store against a real server; the wire format is StackExchange.Redis' own.</summary>
[RequiresDocker]
public sealed class RedisSnapshotStoreTests
{
    // renovate: datasource=docker depName=redis
    private const string Image = "redis:8.2-alpine";

    private static RedisContainer? _container;
    private static IConnectionMultiplexer? _redis;

    [Before(Class)]
    public static async Task StartRedis()
    {
        _container = new RedisBuilder(Image).Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    [After(Class)]
    public static async Task StopRedis()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task Inputs_Round_Trip_Through_Redis()
    {
        RedisSnapshotStore store = Store(out _);

        await store.WriteAsync(Inputs(), CancellationToken.None);
        StoredInputs? restored = await store.ReadAsync(CancellationToken.None);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Repos[0].Repo).IsEqualTo("dutchy/folio");
        await Assert.That(restored.Repos[0].PinnedSha).IsEqualTo("abc123");
        await Assert.That(restored.CapturedAt).IsEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public async Task File_Contents_Survive_The_Round_Trip()
    {
        RedisSnapshotStore store = Store(out _);
        await store.WriteAsync(Inputs(), CancellationToken.None);

        StoredInputs? restored = await store.ReadAsync(CancellationToken.None);
        bool found = restored!.Repos[0].Files.TryGet(".folio/project.toml", out ReadOnlyMemory<byte> contents);

        await Assert.That(found).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(contents.Span)).IsEqualTo("version = 1\n");
    }

    [Test]
    public async Task Reports_Nothing_When_The_Key_Was_Never_Written()
    {
        await Assert.That(await Store(out _).ReadAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task A_Second_Write_Replaces_The_First()
    {
        RedisSnapshotStore store = Store(out _);

        await store.WriteAsync(Inputs(), CancellationToken.None);
        await store.WriteAsync(Inputs() with { CentralSha = "later-sha" }, CancellationToken.None);

        StoredInputs? restored = await store.ReadAsync(CancellationToken.None);

        await Assert.That(restored!.CentralSha).IsEqualTo("later-sha");
    }

    [Test]
    public async Task A_Value_That_Is_Not_A_Snapshot_Reads_As_Nothing()
    {
        RedisSnapshotStore store = Store(out string key);
        _ = await _redis!.GetDatabase().StringSetAsync(key, "{ not json");

        await Assert.That(await store.ReadAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task An_Empty_Value_Reads_As_Nothing()
    {
        RedisSnapshotStore store = Store(out string key);
        _ = await _redis!.GetDatabase().StringSetAsync(key, string.Empty);

        await Assert.That(await store.ReadAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task A_Cancelled_Read_Still_Cancels()
    {
        await Assert.That(async () => await Store(out _).ReadAsync(new CancellationToken(canceled: true)))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task A_Cancelled_Write_Still_Cancels()
    {
        await Assert.That(async () => await Store(out _).WriteAsync(Inputs(), new CancellationToken(canceled: true)))
            .Throws<OperationCanceledException>();
    }

    // Tests share one server, so each takes its own key rather than serializing the class.
    private static RedisSnapshotStore Store(out string key)
    {
        key = $"folio:{Guid.NewGuid():N}";
        return new RedisSnapshotStore(_redis!, key, NullLogger<RedisSnapshotStore>.Instance);
    }

    internal static StoredInputs Inputs() => new(
        "dutchy/portfolio",
        "central-sha",
        new FileSet([new(".folio/site.toml", Encoding.UTF8.GetBytes("version = 1\n"))]),
        [
            new RepoInput(
                "dutchy/folio",
                Path: string.Empty,
                PinnedSha: "abc123",
                Files: new FileSet([new(".folio/project.toml", Encoding.UTF8.GetBytes("version = 1\n"))]),
                Metadata: new RepoMetadata(
                    "dutchy", "folio", "A repo", null, ["cli"], "Rust",
                    [new RepoLanguage("Rust", 10)],
                    1, 2, "MIT", false,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []),
                MediaSizes: new Dictionary<string, MediaSize>(StringComparer.Ordinal)
                {
                    [".folio/media/hero.png"] = new(1280, 720),
                },
                MediaPaths: new HashSet<string>(StringComparer.Ordinal) { ".folio/media/hero.png" }),
        ],
        DateTimeOffset.UnixEpoch);
}

/// <summary>The store's behaviour when Redis cannot be reached, which needs no server to provoke.</summary>
public sealed class RedisSnapshotStoreOutageTests
{
    [Test]
    public async Task An_Unreachable_Server_Reads_As_Nothing_Rather_Than_Throwing()
    {
        await using IConnectionMultiplexer dead = await Unreachable();
        RedisSnapshotStore store = new(dead, "folio:inputs", NullLogger<RedisSnapshotStore>.Instance);

        await Assert.That(await store.ReadAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task An_Unreachable_Server_Abandons_The_Write_Rather_Than_Throwing()
    {
        await using IConnectionMultiplexer dead = await Unreachable();
        RedisSnapshotStore store = new(dead, "folio:inputs", NullLogger<RedisSnapshotStore>.Instance);

        // The store is an optimization, so a storage failure must not fail the refresh that published.
        await store.WriteAsync(RedisSnapshotStoreTests.Inputs(), CancellationToken.None);
    }

    [Test]
    public async Task Null_Inputs_Are_Rejected()
    {
        await using IConnectionMultiplexer dead = await Unreachable();
        RedisSnapshotStore store = new(dead, "folio:inputs", NullLogger<RedisSnapshotStore>.Instance);

        await Assert.That(async () => await store.WriteAsync(null!, CancellationToken.None))
            .Throws<ArgumentNullException>();
    }

    // Port 1 is reserved and never listening; AbortOnConnectFail defers the failure to the command.
    private static Task<ConnectionMultiplexer> Unreachable() => ConnectionMultiplexer.ConnectAsync(
        new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 1 } },
            AbortOnConnectFail = false,
            ConnectRetry = 0,
            ConnectTimeout = 250,
            SyncTimeout = 250,
        });
}
