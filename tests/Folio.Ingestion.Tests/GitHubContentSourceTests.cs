using System.Net;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Folio.Ingestion.Snapshots;
using Loom.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace Folio.Ingestion.Tests;

public sealed class GitHubContentSourceTests
{
    private const string CentralSite = """
        version = 1

        [site]
        url            = "https://dutchy.dev"
        default_locale = "en"
        locales        = ["en"]
        owner          = "dutchy"
        """;

    [Test]
    public async Task A_Fetch_Collects_The_Central_Config_And_Every_Listed_Project()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos).Count().IsEqualTo(1);
        await Assert.That(result.Inputs.Repos[0].Repo).IsEqualTo("dutchy/folio");
        await Assert.That(result.Inputs.Repos[0].Files.Paths).Contains(".folio/project.toml");
    }

    [Test]
    public async Task Only_Files_Under_The_Folio_Root_Are_Fetched()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new()
            {
                [".folio/project.toml"] = "version = 1\n",
                ["src/Program.cs"] = "class P {}",
                ["README.md"] = "# folio\n",
            });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos[0].Files.Paths).IsEquivalentTo([".folio/project.toml"]);
    }

    [Test]
    public async Task The_Readme_Is_Fetched_Only_When_Opted_In()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\nuse_readme = true\n")
            .Repo("dutchy/folio", new()
            {
                [".folio/project.toml"] = "version = 1\n",
                ["README.md"] = "# folio\n",
                ["docs/README.md"] = "# nested\n",
            });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos[0].Files.Paths)
            .IsEquivalentTo([".folio/project.toml", "README.md"]);
    }

    [Test]
    public async Task Github_Metadata_Is_Carried_Through()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" }, archived: true);

        RepoMetadata metadata = (await Fetch(stub)).Inputs.Repos[0].Metadata;

        await Assert.That(metadata.Stars).IsEqualTo(12);
        await Assert.That(metadata.License).IsEqualTo("MIT");
        await Assert.That(metadata.IsArchived).IsTrue();
        await Assert.That(metadata.Topics).IsEquivalentTo(["cli", "rust"], CollectionOrdering.Matching);
        await Assert.That(metadata.Languages[0].Name).IsEqualTo("Rust");
    }

    [Test]
    public async Task A_Missing_Repository_Drops_That_Project_And_Reports_It()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n\n[[projects]]\nrepo = \"gone\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" })
            .Status("/repos/dutchy/gone", HttpStatusCode.NotFound);

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos.Select(repo => repo.Repo)).IsEquivalentTo(["dutchy/folio"]);
        await Assert.That(result.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.ProjectNotFound);
    }

    [Test]
    public async Task A_Listed_Path_That_Does_Not_Exist_Drops_That_Project()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\npath = \"packages/cli\"\n")
            .Repo("dutchy/folio", new() { ["README.md"] = "# folio\n" });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos).IsEmpty();
        await Assert.That(result.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.ProjectNotFound);
    }

    [Test]
    public async Task A_Truncated_Tree_Drops_That_Project_Rather_Than_Reading_Part_Of_It()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" }, truncated: true);

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos).IsEmpty();
        await Assert.That(result.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.ProjectTreeTruncated);
    }

    [Test]
    public async Task A_Timeout_Abandons_The_Fetch_Rather_Than_Dropping_The_Project()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" })
            .Throws("/repos/dutchy/folio/languages", () => new TaskCanceledException("timed out"));

        Result<FetchResult> result = await Run(stub);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Category).IsEqualTo(ErrorCategory.Unavailable);
    }

    [Test]
    public async Task A_Timeout_On_The_Budget_Check_Abandons_The_Fetch()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Throws("/rate_limit", () => new TaskCanceledException("timed out"));

        Result<FetchResult> result = await Run(stub);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Category).IsEqualTo(ErrorCategory.Unavailable);
    }

    [Test]
    public async Task A_Timeout_On_The_Central_Read_Abandons_The_Fetch()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Throws("/repos/dutchy/portfolio", () => new TaskCanceledException("timed out"));

        Result<FetchResult> result = await Run(stub);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Category).IsEqualTo(ErrorCategory.Unavailable);
    }

    [Test]
    public async Task A_Metadata_Server_Error_Abandons_The_Fetch()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" })
            .Status("/repos/dutchy/folio/languages", HttpStatusCode.InternalServerError);

        Result<FetchResult> result = await Run(stub);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Category).IsEqualTo(ErrorCategory.Unavailable);
    }

    [Test]
    public async Task A_Server_Error_Abandons_The_Whole_Fetch()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Status("/repos/dutchy/folio", HttpStatusCode.BadGateway);

        Result<FetchResult> result = await Run(stub);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Category).IsEqualTo(ErrorCategory.Unavailable);
    }

    [Test]
    public async Task Too_Little_Budget_Abandons_Before_Any_Repository_Is_Read()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n");
        stub.RateLimit(10);

        Result<FetchResult> result = await Run(stub, minimumBudget: 500);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(stub.Requests.Any(path => path.StartsWith("/repos/dutchy/folio", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task Projects_Keep_The_Order_The_Central_List_Declares()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"a\"\n\n[[projects]]\nrepo = \"b\"\n\n[[projects]]\nrepo = \"c\"\n")
            .Repo("dutchy/a", new() { [".folio/project.toml"] = "version = 1\n" })
            .Repo("dutchy/b", new() { [".folio/project.toml"] = "version = 1\n" })
            .Repo("dutchy/c", new() { [".folio/project.toml"] = "version = 1\n" });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos.Select(repo => repo.Repo))
            .IsEquivalentTo(["dutchy/a", "dutchy/b", "dutchy/c"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Images_Are_Measured_And_Their_Sizes_Carried()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project.media]\nhero = \".folio/media/hero.png\"\n",
                [".folio/media/hero.png"] = "fake",
            });

        StubMediaProbe probe = new(new MediaSize(1280, 720));
        FetchResult result = await Fetch(stub, probe);

        await Assert.That(probe.Measured).IsEquivalentTo([".folio/media/hero.png"]);
        await Assert.That(result.Inputs.Repos[0].MediaSizes[".folio/media/hero.png"].Width).IsEqualTo(1280);
    }

    [Test]
    public async Task Media_Outside_The_Folio_Directory_Is_Known_And_Measured_But_Not_Fetched()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project.media]\nhero = \"docs/shot.png\"\n",
                ["docs/shot.png"] = "img",
            });

        StubMediaProbe probe = new(new MediaSize(640, 480));
        FetchResult result = await Fetch(stub, probe);

        await Assert.That(result.Inputs.Repos[0].MediaPaths).Contains("docs/shot.png");
        await Assert.That(probe.Measured).IsEquivalentTo(["docs/shot.png"]);
        await Assert.That(result.Inputs.Repos[0].Files.Paths).DoesNotContain("docs/shot.png");
    }

    [Test]
    public async Task A_Path_Project_Fetches_Its_Own_Readme_Not_The_Roots()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\npath = \"packages/cli\"\nuse_readme = true\n")
            .Repo("dutchy/folio", new()
            {
                ["README.md"] = "# root\n",
                ["packages/cli/README.md"] = "# cli\n",
                ["packages/cli/.folio/project.toml"] = "version = 1\n",
            });

        FetchResult result = await Fetch(stub);

        await Assert.That(result.Inputs.Repos[0].Files.Paths).Contains("packages/cli/README.md");
        await Assert.That(result.Inputs.Repos[0].Files.Paths).DoesNotContain("README.md");
    }

    [Test]
    public async Task Non_Images_Are_Never_Measured()
    {
        GitHubStub stub = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });

        StubMediaProbe probe = new(new MediaSize(1, 1));
        _ = await Fetch(stub, probe);

        await Assert.That(probe.Measured).IsEmpty();
    }

    [Test]
    public async Task A_Blob_Already_Held_At_The_Same_Sha_Is_Not_Fetched_Again()
    {
        GitHubStub first = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });

        StoredInputs previous = (await Fetch(first)).Inputs;

        GitHubStub second = Central("[[projects]]\nrepo = \"folio\"\n")
            .Repo("dutchy/folio", new() { [".folio/project.toml"] = "version = 1\n" });

        _ = await Fetch(second, previous: previous);

        await Assert.That(second.Requests.Any(path =>
            path.StartsWith("/repos/dutchy/folio/git/blobs/", StringComparison.Ordinal))).IsFalse();
    }

    private static GitHubStub Central(string projects)
    {
        GitHubStub stub = new();
        stub.RateLimit(4000);

        return stub.Repo("dutchy/portfolio", new()
        {
            [".folio/site.toml"] = CentralSite,
            [".folio/projects.toml"] = "version = 1\n\n" + projects,
            [".folio/tags.toml"] = "version = 1\n",
        });
    }

    private static async Task<FetchResult> Fetch(
        GitHubStub stub,
        StubMediaProbe? probe = null,
        StoredInputs? previous = null)
    {
        Result<FetchResult> result = await Run(stub, probe: probe, previous: previous);

        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Message);
    }

    private static async Task<Result<FetchResult>> Run(
        GitHubStub stub,
        int minimumBudget = 100,
        StubMediaProbe? probe = null,
        StoredInputs? previous = null) =>
        await new GitHubContentSource(
            stub.Client(),
            probe ?? new StubMediaProbe(),
            new FetchSettings("dutchy/portfolio", CentralRef: null, FetchConcurrency: 4, minimumBudget),
            NullLogger<GitHubContentSource>.Instance)
            .FetchAsync(previous, CancellationToken.None);
}
