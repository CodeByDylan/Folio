using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Folio.Api.Tests;

public sealed class ContractTests
{
    [Test]
    public async Task The_Etag_Varies_By_Endpoint_So_A_Cached_Client_Cannot_Get_304_For_A_Missing_Project()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage site = await client.GetAsync(new Uri("/v1/site", UriKind.Relative));

        using HttpRequestMessage ghost = new(HttpMethod.Get, "/v1/projects/ghost");
        ghost.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(site.Headers.ETag!.Tag));

        using HttpResponseMessage response = await client.SendAsync(ghost);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("/v1/projects/ghost", HttpStatusCode.NotFound)]
    [Arguments("/v1/projects/My_Project", HttpStatusCode.BadRequest)]
    public async Task A_Wildcard_If_None_Match_Cannot_Answer_For_A_Resource_That_Does_Not_Exist(
        string path,
        HttpStatusCode expected)
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Headers.CacheControl?.Public).IsNotEqualTo(true);
    }

    [Test]
    public async Task A_Weak_If_None_Match_Still_Revalidates()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/v1/site", UriKind.Relative));

        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/site");
        request.Headers.TryAddWithoutValidation("If-None-Match", $"W/{first.Headers.ETag!.Tag}");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task A_Head_Request_Is_Accepted_And_Carries_The_Validators()
    {
        // The route accepts HEAD (a plain GET-only route would 405); Kestrel drops the body in production.
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpRequestMessage request = new(HttpMethod.Head, "/v1/site");
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Content.Headers.LastModified).IsNotNull();
    }

    [Test]
    public async Task A_Wildcard_If_None_Match_Is_304()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/site");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task A_Comma_Joined_If_None_Match_Is_304()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/v1/site", UriKind.Relative));

        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/site");
        request.Headers.TryAddWithoutValidation("If-None-Match", $"\"other\", {first.Headers.ETag!.Tag}");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task Content_Responses_Are_Cacheable_And_Do_Not_Vary_On_Accept_Language()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/site", UriKind.Relative));

        await Assert.That(response.Headers.CacheControl!.Public).IsTrue();
        await Assert.That(response.Headers.CacheControl.MaxAge).IsEqualTo(TimeSpan.FromSeconds(60));
        await Assert.That(response.Headers.Vary).IsEmpty();
    }

    [Test]
    public async Task A_Malformed_Slug_Is_400_Rather_Than_404()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/projects/My_Project", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Concurrent_Triggers_All_Succeed()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = app.CreateClient();

        HttpStatusCode[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => app.RefreshAsync(client)));

        // Whether they joined depends on whether they overlapped; RefreshGateTests pins the joining.
        await Assert.That(outcomes).IsEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK]);
        await Assert.That(app.Source.Fetches).IsGreaterThanOrEqualTo(1);

        using HttpResponseMessage projects = await client.GetAsync(new Uri("/v1/projects", UriKind.Relative));
        await Assert.That(projects.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/v1/diagnostics?severity=")]
    [Arguments("/v1/diagnostics?severity=0")]
    [Arguments("/v1/diagnostics?severity=bogus")]
    [Arguments("/v1/diagnostics?project=")]
    [Arguments("/v1/diagnostics?project=My_Project")]
    public async Task A_Malformed_Filter_Is_400(string path)
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Diagnostics_Can_Be_Filtered_By_Project()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        HttpClient client = await app.ReadyAsync();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri("/v1/diagnostics?project=folio", UriKind.Relative));
        using HttpResponseMessage none =
            await client.GetAsync(new Uri("/v1/diagnostics?project=ghost", UriKind.Relative));
        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using JsonDocument empty = JsonDocument.Parse(await none.Content.ReadAsStringAsync());

        string[] projects =
        [
            .. parsed.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(diagnostic => diagnostic.GetProperty("project").GetString() ?? string.Empty),
        ];

        // An all-match over an empty list is vacuously true, so both halves have to be asserted.
        await Assert.That(projects).IsNotEmpty();
        await Assert.That(projects.Distinct()).IsEquivalentTo(["folio"]);
        await Assert.That(empty.RootElement.GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
    }
}
