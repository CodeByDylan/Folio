using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Loom.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace Folio.Ingestion.Tests;

public sealed class ReleaseTests
{
    [Test]
    public async Task Releases_Are_Newest_First()
    {
        IReadOnlyList<RepoRelease> releases = await Fetch(
            ("v1.0.0", "2026-01-01T00:00:00Z", false, false),
            ("v2.0.0", "2026-06-01T00:00:00Z", false, false),
            ("v1.5.0", "2026-03-01T00:00:00Z", false, false));

        await Assert.That(releases.Select(release => release.TagName))
            .IsEquivalentTo(["v2.0.0", "v1.5.0", "v1.0.0"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Releases_Published_At_The_Same_Instant_Are_Ordered_By_Tag()
    {
        IReadOnlyList<RepoRelease> releases = await Fetch(
            ("v1.0.1", "2026-01-01T00:00:00Z", false, false),
            ("v1.0.0", "2026-01-01T00:00:00Z", false, false));

        // Byte-stable output needs a tie-break, or two identical builds can disagree.
        await Assert.That(releases.Select(release => release.TagName)).IsEquivalentTo(["v1.0.0", "v1.0.1"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Drafts_Are_Excluded()
    {
        IReadOnlyList<RepoRelease> releases = await Fetch(
            ("v1.0.0", "2026-01-01T00:00:00Z", false, false),
            ("v2.0.0-draft", null, true, false));

        await Assert.That(releases.Select(release => release.TagName)).IsEquivalentTo(["v1.0.0"]);
    }

    [Test]
    public async Task Prereleases_Are_Kept_And_Flagged()
    {
        IReadOnlyList<RepoRelease> releases = await Fetch(
            ("v2.0.0-rc1", "2026-06-01T00:00:00Z", false, true),
            ("v1.0.0", "2026-01-01T00:00:00Z", false, false));

        await Assert.That(releases[0].TagName).IsEqualTo("v2.0.0-rc1");
        await Assert.That(releases[0].IsPrerelease).IsTrue();
        await Assert.That(releases[1].IsPrerelease).IsFalse();
    }

    [Test]
    public async Task A_Repository_With_No_Releases_Reports_An_Empty_List()
    {
        await Assert.That(await Fetch()).IsEmpty();
    }

    [Test]
    public async Task Release_Urls_Point_At_Their_Github_Page()
    {
        IReadOnlyList<RepoRelease> releases = await Fetch(("v1.0.0", "2026-01-01T00:00:00Z", false, false));

        await Assert.That(releases[0].Url.ToString())
            .IsEqualTo("https://github.com/dutchy/folio/releases/tag/v1.0.0");
    }

    [Test]
    public async Task Releases_Cost_One_Request_Per_Repository()
    {
        GitHubStub stub = Stub([("v1.0.0", "2026-01-01T00:00:00Z", false, false)]);
        _ = await Run(stub);

        await Assert.That(stub.Requests.Count(path => path == "/repos/dutchy/folio/releases")).IsEqualTo(1);
    }

    private static async Task<IReadOnlyList<RepoRelease>> Fetch(
        params (string Tag, string? PublishedAt, bool Draft, bool Prerelease)[] releases)
    {
        FetchResult result = await Run(Stub(releases));

        return result.Inputs.Repos[0].Metadata.Releases;
    }

    private static GitHubStub Stub((string Tag, string? PublishedAt, bool Draft, bool Prerelease)[] releases)
    {
        GitHubStub stub = new();
        stub.RateLimit(4000);

        _ = stub.Repo("dutchy/portfolio", new()
        {
            [".folio/site.toml"] = """
                version = 1

                [site]
                url            = "https://dutchy.dev"
                default_locale = "en"
                locales        = ["en"]
                owner          = "dutchy"
                """,
            [".folio/projects.toml"] = "version = 1\n\n[[projects]]\nrepo = \"folio\"\n",
            [".folio/tags.toml"] = "version = 1\n",
        });

        _ = stub.Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });

        return releases.Length == 0 ? stub : stub.Releases("dutchy/folio", releases);
    }

    private static async Task<FetchResult> Run(GitHubStub stub)
    {
        Result<FetchResult> result = await new GitHubContentSource(
            stub.Client(),
            new StubMediaProbe(),
            new FetchSettings("dutchy/portfolio", CentralRef: null, FetchConcurrency: 4, MinimumRateLimitBudget: 100),
            NullLogger<GitHubContentSource>.Instance)
            .FetchAsync(previous: null, CancellationToken.None);

        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Message);
    }
}
