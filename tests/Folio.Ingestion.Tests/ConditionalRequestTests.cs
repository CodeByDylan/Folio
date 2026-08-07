using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Loom.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace Folio.Ingestion.Tests;

public sealed class ConditionalRequestTests
{
    [Test]
    public async Task The_First_Fetch_Sends_No_Conditional_Header()
    {
        GitHubStub stub = Stub();
        _ = await Fetch(stub, new EtagCache());

        await Assert.That(stub.Conditional).IsNotEmpty();
        await Assert.That(stub.Conditional.All(request => request.IfNoneMatch is null)).IsTrue();
    }

    [Test]
    public async Task A_Later_Fetch_Revalidates_Repository_Metadata()
    {
        EtagCache cache = new();
        GitHubStub first = Stub();
        _ = await Fetch(first, cache);

        GitHubStub second = Stub();
        _ = await Fetch(second, cache);

        (string Path, string? IfNoneMatch) metadata = second.Conditional
            .First(request => request.Path == "/repos/dutchy/folio");

        await Assert.That(metadata.IfNoneMatch).IsNotNull();
    }

    [Test]
    public async Task A_304_Still_Produces_The_Same_Metadata()
    {
        EtagCache cache = new();
        RepoMetadata first = (await Fetch(Stub(), cache)).Inputs.Repos[0].Metadata;
        RepoMetadata second = (await Fetch(Stub(), cache)).Inputs.Repos[0].Metadata;

        await Assert.That(second.Stars).IsEqualTo(first.Stars);
        await Assert.That(second.License).IsEqualTo(first.License);
        await Assert.That(second.Topics).IsEquivalentTo(first.Topics);
        await Assert.That(second.Languages).IsEquivalentTo(first.Languages, CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Rate_Limit_Check_Is_Never_Revalidated()
    {
        EtagCache cache = new();
        _ = await Fetch(Stub(), cache);

        GitHubStub second = Stub();
        _ = await Fetch(second, cache);

        // A cached budget would report a figure that has already been spent.
        (string Path, string? IfNoneMatch)[] budget =
            [.. second.Conditional.Where(request => request.Path == "/rate_limit")];

        await Assert.That(budget).IsNotEmpty();
        await Assert.That(budget.All(request => request.IfNoneMatch is null)).IsTrue();
    }

    [Test]
    public async Task Nothing_Is_Cached_When_Github_Sends_No_Etag()
    {
        EtagCache cache = new();
        GitHubStub stub = Stub();
        stub.SupportsEtags = false;

        _ = await Fetch(stub, cache);

        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Each_Url_Is_Cached_Separately()
    {
        EtagCache cache = new();
        _ = await Fetch(Stub(), cache);

        // Central and project repositories, their trees, and the language and release endpoints.
        await Assert.That(cache.Count).IsGreaterThan(4);
    }

    private static GitHubStub Stub()
    {
        GitHubStub stub = new() { SupportsEtags = true };
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

        return stub.Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });
    }

    private static async Task<FetchResult> Fetch(GitHubStub stub, EtagCache cache)
    {
        Result<FetchResult> result = await new GitHubContentSource(
            stub.ConditionalClient(cache),
            new StubMediaProbe(),
            new FetchSettings("dutchy/portfolio", CentralRef: null, FetchConcurrency: 4, MinimumRateLimitBudget: 100,
                MaxFileBytes: int.MaxValue, MaxFileCount: int.MaxValue, MaxTotalBytes: long.MaxValue),
            NullLogger<GitHubContentSource>.Instance)
            .FetchAsync(previous: null, CancellationToken.None);

        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Message);
    }
}
