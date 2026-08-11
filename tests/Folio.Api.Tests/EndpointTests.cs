using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Folio.Api.Tests;

public sealed class EndpointTests
{
    [Test]
    public async Task Content_Endpoints_Answer_503_Before_The_First_Build()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task The_Site_Endpoint_Returns_Site_Facts_And_Its_Pages_Without_Bodies()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement site = await Json(client, "/v1/site?locale=nl");
        JsonElement home = site.GetProperty("pages")[0];

        await Assert.That(site.GetProperty("locale").GetString()).IsEqualTo("nl");
        await Assert.That(site.GetProperty("url").GetString()).IsEqualTo("https://dutchy.dev/");
        await Assert.That(site.GetProperty("locales").EnumerateArray().Select(l => l.GetString()!))
            .IsEquivalentTo(["en", "nl"]);
        await Assert.That(site.TryGetProperty("sections", out _)).IsFalse();
        await Assert.That(home.GetProperty("slug").GetString()).IsEqualTo("home");
        await Assert.That(home.GetProperty("home").GetBoolean()).IsTrue();
        await Assert.That(home.GetProperty("navLabel").GetString()).IsEqualTo("Start");
        await Assert.That(home.TryGetProperty("sections", out _)).IsFalse();
    }

    [Test]
    public async Task A_Page_Carries_Its_Sections_With_Their_Type()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement page = await Json(client, "/v1/pages/home?locale=nl");
        JsonElement about = page.GetProperty("sections")[0];

        await Assert.That(page.GetProperty("slug").GetString()).IsEqualTo("home");
        await Assert.That(about.GetProperty("id").GetString()).IsEqualTo("about");
        await Assert.That(about.GetProperty("type").GetString()).IsEqualTo("prose");
        await Assert.That(about.GetProperty("title").GetString()).IsEqualTo("Over mij");
    }

    [Test]
    public async Task An_Unknown_Page_Answers_404()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/pages/nope", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task The_Project_List_Omits_Section_Bodies()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement projects = await Json(client, "/v1/projects");
        JsonElement first = projects.GetProperty("projects")[0];

        await Assert.That(first.GetProperty("slug").GetString()).IsEqualTo("folio");
        await Assert.That(first.GetProperty("featured").GetBoolean()).IsTrue();
        await Assert.That(first.TryGetProperty("sections", out _)).IsFalse();
    }

    [Test]
    public async Task A_Project_Carries_Its_Sections_And_Language_Shares()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement project = await Json(client, "/v1/projects/folio");

        await Assert.That(project.GetProperty("sections")[0].GetProperty("body").GetString())
            .IsEqualTo("Folio reads two sources.");
        await Assert.That(project.GetProperty("metadata").GetProperty("languages")[0].GetProperty("percent").GetDouble())
            .IsEqualTo(90d);
    }

    [Test]
    public async Task Releases_Reach_The_Wire_Newest_First()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement releases = (await Json(client, "/v1/projects/folio"))
            .GetProperty("metadata").GetProperty("releases");

        await Assert.That(releases.GetArrayLength()).IsEqualTo(2);
        await Assert.That(releases[0].GetProperty("tagName").GetString()).IsEqualTo("v2.0.0");
        await Assert.That(releases[0].GetProperty("prerelease").GetBoolean()).IsFalse();
        await Assert.That(releases[1].GetProperty("prerelease").GetBoolean()).IsTrue();
        await Assert.That(releases[1].GetProperty("url").GetString())
            .IsEqualTo("https://github.com/dutchy/folio/releases/tag/v1.0.0");
    }

    [Test]
    public async Task A_Release_Without_A_Title_Omits_It_Rather_Than_Nulling_It()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement releases = (await Json(client, "/v1/projects/folio"))
            .GetProperty("metadata").GetProperty("releases");

        await Assert.That(releases[1].TryGetProperty("name", out _)).IsFalse();
    }

    [Test]
    public async Task The_Project_List_Carries_Releases_Too()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement first = (await Json(client, "/v1/projects")).GetProperty("projects")[0];

        await Assert.That(first.GetProperty("metadata").GetProperty("releases").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task An_Unknown_Slug_Is_404_As_Problem_Details()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/projects/ghost", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
    }

    [Test]
    public async Task Provenance_Is_Flat_And_Sparse()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement english = await Json(client, "/v1/projects/folio?locale=en");
        JsonElement dutch = await Json(client, "/v1/projects/folio?locale=nl");

        await Assert.That(english.GetProperty("provenance").EnumerateObject().Any()).IsFalse();

        // The Dutch bundle has no tagline, so exactly that one field records a fallback.
        JsonElement provenance = dutch.GetProperty("provenance");
        await Assert.That(provenance.GetProperty("/tagline").GetProperty("locale").GetString()).IsEqualTo("en");
        await Assert.That(provenance.GetProperty("/tagline").GetProperty("fallback").GetBoolean()).IsTrue();
        await Assert.That(dutch.GetProperty("tagline").GetString()).IsEqualTo("Assembled portfolios");
    }

    [Test]
    public async Task An_Omitted_Locale_Serves_The_Default()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement site = await Json(client, "/v1/site");

        await Assert.That(site.GetProperty("locale").GetString()).IsEqualTo("en");
    }

    [Test]
    public async Task A_Locale_That_Resolves_By_Truncation_Is_Served()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement site = await Json(client, "/v1/site?locale=nl-BE");

        await Assert.That(site.GetProperty("requestedLocale").GetString()).IsEqualTo("nl-BE");
        await Assert.That(site.GetProperty("locale").GetString()).IsEqualTo("nl");
    }

    [Test]
    public async Task An_Unservable_Locale_Is_400_Rather_Than_A_Silent_Fallback()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/site?locale=pt-BR", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Responses_Carry_A_Strong_Etag_That_Varies_By_Locale()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage english = await client.GetAsync(new Uri("/v1/site?locale=en", UriKind.Relative));
        using HttpResponseMessage dutch = await client.GetAsync(new Uri("/v1/site?locale=nl", UriKind.Relative));

        await Assert.That(english.Headers.ETag!.IsWeak).IsFalse();
        await Assert.That(english.Headers.ETag.Tag).IsNotEqualTo(dutch.Headers.ETag!.Tag);
        await Assert.That(english.Content.Headers.LastModified).IsNotNull();
    }

    [Test]
    public async Task A_Matching_If_None_Match_Is_304()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/v1/site", UriKind.Relative));

        using HttpRequestMessage second = new(HttpMethod.Get, "/v1/site");
        second.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(first.Headers.ETag!.Tag));

        using HttpResponseMessage response = await client.SendAsync(second);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task The_Response_Body_Carries_No_Build_Timestamp()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement site = await Json(client, "/v1/site");

        await Assert.That(site.TryGetProperty("builtAt", out _)).IsFalse();
    }

    [Test]
    public async Task Diagnostics_Are_Separate_From_Content_And_Uncached()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement project = await Json(client, "/v1/projects/folio");
        await Assert.That(project.TryGetProperty("diagnostics", out _)).IsFalse();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/diagnostics", UriKind.Relative));

        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();

        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement counts = parsed.RootElement.GetProperty("counts");

        // Every severity is reported, including zero, so a CI check never reads a missing key.
        await Assert.That(counts.EnumerateObject().Select(entry => entry.Name))
            .IsEquivalentTo(["info", "warning", "error"]);
    }

    [Test]
    public async Task Diagnostics_Can_Be_Filtered_By_Severity()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        JsonElement report = await Json(client, "/v1/diagnostics?severity=info");
        JsonElement[] entries = [.. report.GetProperty("diagnostics").EnumerateArray()];

        await Assert.That(entries).IsNotEmpty();
        await Assert.That(entries.All(d => d.GetProperty("severity").GetString() == "info")).IsTrue();
    }

    [Test]
    public async Task Refresh_Requires_The_Key()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(new Uri("/v1/refresh", UriKind.Relative), null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Reads_Need_No_Key()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Readiness_Reports_Unready_Until_A_Snapshot_Exists()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();

        using HttpResponseMessage before = await client.GetAsync(new Uri("/ready", UriKind.Relative));
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

        using HttpRequestMessage refresh = new(HttpMethod.Post, "/v1/refresh");
        refresh.Headers.Add("X-Folio-Key", FolioApp.RefreshKey);
        _ = await client.SendAsync(refresh);

        using HttpResponseMessage after = await client.GetAsync(new Uri("/ready", UriKind.Relative));
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> Json(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
