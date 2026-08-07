using System.Net;
using System.Text;
using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Folio.Ingestion.Media;
using Octokit;
using Octokit.Internal;

namespace Folio.Ingestion.Tests;

/// <summary>Answers GitHub requests from canned responses, so Octokit and its deserialization run for real.</summary>
internal sealed class GitHubStub : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the path of every request that reached the handler, in arrival order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets the <c>If-None-Match</c> value sent for each path, when one was.</summary>
    public List<(string Path, string? IfNoneMatch)> Conditional { get; } = [];

    /// <summary>Gets whether responses carry an <c>ETag</c> and honour <c>If-None-Match</c>.</summary>
    public bool SupportsEtags { get; set; }

    /// <summary>Gets a client wired to this handler.</summary>
    /// <returns>A real Octokit client.</returns>
    public IGitHubClient Client() =>
        new GitHubClient(new Connection(new ProductHeaderValue("folio-tests"), new HttpClientAdapter(() => this)));

    /// <summary>Gets a client whose transport revalidates through the ETag cache.</summary>
    /// <param name="cache">The cache to share across fetches.</param>
    /// <returns>A real Octokit client behind the conditional transport.</returns>
    public IGitHubClient ConditionalClient(EtagCache cache) =>
        new GitHubClient(new Connection(
            new ProductHeaderValue("folio-tests"),
            new ConditionalHttpClient(new HttpClientAdapter(() => this), cache)));

    /// <summary>Answers a path with JSON.</summary>
    /// <param name="path">The request path, without the host.</param>
    /// <param name="json">The response body.</param>
    /// <returns>This stub.</returns>
    public GitHubStub Json(string path, string json)
    {
        _routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return this;
    }

    /// <summary>Answers a path with a status code.</summary>
    /// <param name="path">The request path, without the host.</param>
    /// <param name="status">The status to return.</param>
    /// <returns>This stub.</returns>
    public GitHubStub Status(string path, HttpStatusCode status)
    {
        _routes[path] = () => new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"stubbed\"}", Encoding.UTF8, "application/json"),
        };

        return this;
    }

    /// <summary>Answers a path by throwing, as a transport fault does.</summary>
    /// <param name="path">The request path, without the host.</param>
    /// <param name="fault">Creates the exception to throw.</param>
    /// <returns>This stub.</returns>
    public GitHubStub Throws(string path, Func<Exception> fault)
    {
        _routes[path] = () => throw fault();
        return this;
    }

    /// <summary>Answers the rate-limit endpoint.</summary>
    /// <param name="remaining">The core budget to report.</param>
    /// <returns>This stub.</returns>
    public GitHubStub RateLimit(int remaining) => Json(
        "/rate_limit",
        $$"""
        { "resources": { "core": { "limit": 5000, "remaining": {{remaining}}, "reset": 1754553600 } },
          "rate": { "limit": 5000, "remaining": {{remaining}}, "reset": 1754553600 } }
        """);

    /// <summary>Answers the repository, tree, blob and language endpoints for one repository.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="files">The files the tree lists, keyed by path.</param>
    /// <param name="archived">Whether GitHub reports it archived.</param>
    /// <param name="truncated">Whether the tree listing is truncated.</param>
    /// <returns>This stub.</returns>
    public GitHubStub Repo(
        string repo,
        Dictionary<string, string> files,
        bool archived = false,
        bool truncated = false)
    {
        string[] parts = repo.Split('/');

        _ = Json($"/repos/{repo}", $$"""
            { "id": 1, "name": "{{parts[1]}}", "full_name": "{{repo}}", "default_branch": "main",
              "description": "A repository.", "homepage": null, "topics": ["rust", "cli"],
              "language": "Rust", "stargazers_count": 12, "forks_count": 3, "archived": {{Lower(archived)}},
              "created_at": "2026-01-01T00:00:00Z", "pushed_at": "2026-06-01T00:00:00Z",
              "license": { "spdx_id": "MIT", "key": "mit", "name": "MIT License" },
              "owner": { "id": 1, "login": "{{parts[0]}}" } }
            """);

        string entries = string.Join(",", files.Select(file => $$"""
            { "path": "{{file.Key}}", "mode": "100644", "type": "blob",
              "sha": "{{Sha(file.Value)}}", "size": {{Encoding.UTF8.GetByteCount(file.Value)}} }
            """));

        _ = Json($"/repos/{repo}/git/trees/main", $$"""
            { "sha": "treesha1", "url": "https://api.github.com/x", "truncated": {{Lower(truncated)}},
              "tree": [{{entries}}] }
            """);

        foreach (string content in files.Values.Distinct(StringComparer.Ordinal))
        {
            _ = Json($"/repos/{repo}/git/blobs/{Sha(content)}", $$"""
                { "sha": "{{Sha(content)}}", "size": {{Encoding.UTF8.GetByteCount(content)}},
                  "encoding": "base64", "content": "{{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}}" }
                """);
        }

        _ = Json($"/repos/{repo}/releases", "[]");

        return Json($"/repos/{repo}/languages", """{ "Rust": 1000, "Shell": 50 }""");
    }

    /// <summary>Answers the releases endpoint.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="releases">Each release as tag, publish date, draft and prerelease flags.</param>
    /// <returns>This stub.</returns>
    public GitHubStub Releases(
        string repo,
        params (string Tag, string? PublishedAt, bool Draft, bool Prerelease)[] releases) =>
        Json($"/repos/{repo}/releases", "[" + string.Join(",", releases.Select((release, index) => $$"""
            { "id": {{index + 1}}, "tag_name": "{{release.Tag}}", "name": "Release {{release.Tag}}",
              "draft": {{Lower(release.Draft)}}, "prerelease": {{Lower(release.Prerelease)}},
              "created_at": "2026-01-01T00:00:00Z",
              "published_at": {{(release.PublishedAt is null ? "null" : $"\"{release.PublishedAt}\"")}},
              "html_url": "https://github.com/{{repo}}/releases/tag/{{release.Tag}}",
              "url": "https://api.github.com/repos/{{repo}}/releases/{{index + 1}}",
              "assets_url": "https://api.github.com/x", "upload_url": "https://uploads.github.com/x",
              "tarball_url": null, "zipball_url": null, "body": "", "target_commitish": "main",
              "assets": [] }
            """)) + "]");

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string? ifNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.Tag;

        lock (Requests)
        {
            Requests.Add(path);
            Conditional.Add((path, ifNoneMatch));
        }

        if (!_routes.TryGetValue(path, out Func<HttpResponseMessage>? route))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"{{\"message\":\"no stub for {path}\"}}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }

        if (!SupportsEtags)
        {
            return Task.FromResult(route());
        }

        string etag = $"\"{path.GetHashCode(StringComparison.Ordinal):x8}\"";

        if (string.Equals(ifNoneMatch, etag, StringComparison.Ordinal))
        {
            HttpResponseMessage notModified = new(HttpStatusCode.NotModified);
            notModified.Headers.TryAddWithoutValidation("ETag", etag);

            return Task.FromResult(notModified);
        }

        HttpResponseMessage response = route();
        response.Headers.TryAddWithoutValidation("ETag", etag);

        return Task.FromResult(response);
    }

    private static string Lower(bool value) => value ? "true" : "false";

    /// <summary>Hashes content the way git addresses a blob, so cache hits behave as they do live.</summary>
    private static string Sha(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");

        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA1.HashData([.. header, .. bytes]));
    }
}

/// <summary>Reports a fixed size for every image.</summary>
internal sealed class StubMediaProbe(MediaSize? size = null) : IMediaProbe
{
    /// <summary>Gets the paths measured.</summary>
    public List<string> Measured { get; } = [];

    /// <inheritdoc />
    public Task<MediaSize?> MeasureAsync(
        string repo,
        string pinnedSha,
        string path,
        CancellationToken cancellationToken)
    {
        lock (Measured)
        {
            Measured.Add(path);
        }

        return Task.FromResult(size);
    }
}
