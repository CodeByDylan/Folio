using System.Net;
using System.Text.Json;
using Folio.Ingestion;
using Microsoft.Extensions.Time.Testing;

namespace Folio.Api.Tests;

public sealed class RefreshServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    [Test]
    public async Task The_Timer_Builds_A_Snapshot_Without_Anyone_Asking()
    {
        FakeTimeProvider clock = new();
        using FolioApp app = new(FolioApp.WorkedExample(), clock: clock, refreshOnTimer: true);
        HttpClient client = app.CreateClient();

        await Polling.Until(async () =>
        {
            using HttpResponseMessage projects = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));

            return projects.StatusCode is HttpStatusCode.OK;
        });
    }

    [Test]
    public async Task The_Loop_Rebuilds_On_Every_Tick()
    {
        FakeTimeProvider clock = new();
        using FolioApp app = new(FolioApp.WorkedExample(), clock: clock, refreshOnTimer: true);
        _ = app.CreateClient();

        await Ticks(app, 1);

        clock.Advance(Interval);
        await Ticks(app, 2);

        clock.Advance(Interval);
        await Ticks(app, 3);
    }

    [Test]
    public async Task A_Failing_Rebuild_Does_Not_Stop_The_Loop()
    {
        FakeTimeProvider clock = new();
        using FolioApp app = new(
            FolioApp.WorkedExample(),
            FolioIngestionErrors.Transient(new HttpRequestException("down")),
            clock,
            refreshOnTimer: true);

        HttpClient client = app.CreateClient();

        await Ticks(app, 1);

        // The first tick failed; the loop must still be running to reach the second.
        clock.Advance(Interval);
        await Ticks(app, 2);

        await Polling.Until(async () => await Outcome(client) is "abandoned-transient");
    }

    [Test]
    public async Task A_Throwing_Rebuild_Does_Not_Stop_The_Loop()
    {
        FakeTimeProvider clock = new();
        using FolioApp app = new(FolioApp.WorkedExample(), clock: clock, refreshOnTimer: true);
        HttpClient client = app.CreateClient();
        app.Source.StartThrowing(new InvalidOperationException("the source is broken"));

        await Ticks(app, 1);

        // Nothing catches this below the loop, so only its own guard can keep the timer alive.
        clock.Advance(Interval);
        await Ticks(app, 2);

        await Polling.Until(async () => await Outcome(client) is "abandoned-transient");
    }

    private static Task Ticks(FolioApp app, int count) =>
        Polling.Until(() => app.Source.Fetches >= count);

    private static async Task<string?> Outcome(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        using JsonDocument report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return report.RootElement.TryGetProperty("lastRefresh", out JsonElement last)
            && last.ValueKind is not JsonValueKind.Null
            ? last.GetProperty("outcome").GetString()
            : null;
    }

}
