using System.Net;
using System.Text.Json;
using Folio.Domain.Diagnostics;
using Folio.Ingestion;
using Microsoft.Extensions.Time.Testing;

namespace Folio.Api.Tests;

public sealed class RefreshDiagnosticsTests
{
    [Test]
    public async Task A_Transient_Fault_Is_Reported_As_An_Abandoned_Refresh()
    {
        JsonElement report = await ReportAfterFailedRefresh(
            FolioIngestionErrors.Transient(new HttpRequestException("GitHub is unreachable")));

        await Assert.That(Codes(report)).Contains(DiagnosticCodes.RefreshAbandoned);
        await Assert.That(report.GetProperty("lastRefresh").GetProperty("outcome").GetString())
            .IsEqualTo("abandoned-transient");
    }

    [Test]
    public async Task An_Exhausted_Budget_Is_Reported_Under_Its_Own_Code()
    {
        JsonElement report = await ReportAfterFailedRefresh(
            FolioIngestionErrors.RateLimitInsufficient(remaining: 10, required: 500));

        await Assert.That(Codes(report)).Contains(DiagnosticCodes.RefreshRateLimitInsufficient);
    }

    [Test]
    public async Task Diagnostics_Answer_Even_When_No_Snapshot_Was_Ever_Built()
    {
        JsonElement report = await ReportAfterFailedRefresh(
            FolioIngestionErrors.Transient(new HttpRequestException("down")));

        // The snapshot never existed, so this endpoint is the only place that says why.
        await Assert.That(report.TryGetProperty("builtAt", out _)).IsFalse();
        await Assert.That(report.GetProperty("diagnostics").GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task A_Manual_Refresh_Is_Bounded_By_The_Configured_Timeout()
    {
        FakeTimeProvider clock = new();
        using FolioApp app = new(FolioApp.WorkedExample(), clock: clock);
        HttpClient client = app.CreateClient();
        app.Source.StartHanging();

        Task<HttpStatusCode> refreshing = app.RefreshAsync(client);

        await Polling.Until(() => app.Source.Fetches > 0);

        clock.Advance(TimeSpan.FromMinutes(10));
        HttpStatusCode refreshed = await refreshing.WaitAsync(TimeSpan.FromSeconds(10));

        using HttpResponseMessage diagnostics = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        using JsonDocument report = JsonDocument.Parse(await diagnostics.Content.ReadAsStringAsync());

        await Assert.That(refreshed).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(report.RootElement.GetProperty("lastRefresh").GetProperty("outcome").GetString())
            .IsEqualTo("abandoned-transient");
    }

    [Test]
    public async Task A_Boot_With_GitHub_Unreachable_Serves_From_Stored_Inputs()
    {
        using FolioApp app = new(null, FolioIngestionErrors.Transient(new HttpRequestException("down")));
        await app.Store.WriteAsync(FolioApp.WorkedExample(), CancellationToken.None);
        HttpClient client = app.CreateClient();

        HttpStatusCode refreshed = await app.RefreshAsync(client);
        using HttpResponseMessage projects = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));
        using HttpResponseMessage diagnostics = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        using JsonDocument report = JsonDocument.Parse(await diagnostics.Content.ReadAsStringAsync());

        await Assert.That(refreshed).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(projects.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // BuiltAt reflects when the served inputs were captured, not this boot.
        await Assert.That(report.RootElement.GetProperty("builtAt").GetDateTimeOffset())
            .IsEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public async Task An_Abandoned_Refresh_Does_Not_Leak_Raw_Exception_Text()
    {
        const string secret = "internal-host-9000.corp.local";
        using FolioApp app = new(null, FolioIngestionErrors.Transient(new HttpRequestException(secret)));
        HttpClient client = app.CreateClient();

        _ = await app.RefreshAsync(client);
        using HttpResponseMessage diagnostics = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        string body = await diagnostics.Content.ReadAsStringAsync();

        await Assert.That(body).DoesNotContain(secret);
    }

    [Test]
    public async Task A_Boot_With_GitHub_Unreachable_And_Nothing_Stored_Still_Refuses()
    {
        using FolioApp app = new(null, FolioIngestionErrors.Transient(new HttpRequestException("down")));
        HttpClient client = app.CreateClient();

        _ = await app.RefreshAsync(client);
        using HttpResponseMessage projects = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));

        await Assert.That(projects.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task A_Store_That_Cannot_Write_Does_Not_Undo_A_Published_Snapshot()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();
        app.Store.StartThrowingOnWrite();

        HttpStatusCode refreshed = await app.RefreshAsync(client);
        using HttpResponseMessage projects = await client.GetAsync("/v1/projects");

        await Assert.That(refreshed).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(projects.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Content_Endpoints_Still_Refuse_While_Diagnostics_Explain_Why()
    {
        using FolioApp app = new(null, FolioIngestionErrors.Transient(new HttpRequestException("down")));
        HttpClient client = app.CreateClient();

        _ = await Refresh(client);

        using HttpResponseMessage projects = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));
        using HttpResponseMessage diagnostics = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));

        await Assert.That(projects.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(diagnostics.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_Failed_Refresh_Leaves_The_Previous_Snapshot_Serving()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();

        _ = await Refresh(client);
        JsonElement before = await Projects(client);

        app.Source.StartFailing(FolioIngestionErrors.Transient(new HttpRequestException("down")));

        using HttpResponseMessage second = await Refresh(client);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

        JsonElement after = await Projects(client);

        await Assert.That(after.GetProperty("projects").GetArrayLength())
            .IsEqualTo(before.GetProperty("projects").GetArrayLength());

        using HttpResponseMessage report = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        using JsonDocument parsed = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        JsonElement json = parsed.RootElement.Clone();

        await Assert.That(Codes(json)).Contains(DiagnosticCodes.RefreshAbandoned);
        await Assert.That(json.TryGetProperty("builtAt", out _)).IsTrue();
    }

    private static async Task<JsonElement> Projects(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return parsed.RootElement.Clone();
    }

    [Test]
    public async Task A_Successful_Refresh_Reports_Success_And_Adds_No_Diagnostics()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement report = parsed.RootElement.Clone();

        await Assert.That(report.GetProperty("lastRefresh").GetProperty("outcome").GetString()).IsEqualTo("succeeded");
        await Assert.That(Codes(report)).DoesNotContain(DiagnosticCodes.RefreshAbandoned);
    }

    private static async Task<JsonElement> ReportAfterFailedRefresh(Loom.Results.Error failure)
    {
        using FolioApp app = new(null, failure);
        HttpClient client = app.CreateClient();

        using HttpResponseMessage refresh = await Refresh(client);
        await Assert.That(refresh.IsSuccessStatusCode).IsFalse();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return parsed.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> Refresh(HttpClient client)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/refresh");
        request.Headers.Add("X-Folio-Key", FolioApp.RefreshKey);

        return await client.SendAsync(request);
    }

    private static IEnumerable<string> Codes(JsonElement report) =>
        report.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()!);
}
