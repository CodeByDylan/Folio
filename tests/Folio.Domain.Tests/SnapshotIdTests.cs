using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class SnapshotIdTests
{
    [Test]
    public async Task Identical_Inputs_Produce_The_Same_Id()
    {
        await Assert.That(Id(Metadata())).IsEqualTo(Id(Metadata()));
    }

    [Test]
    public async Task The_Central_Pinned_Commit_Changes_The_Id()
    {
        var portfolio = Portfolio.Valid().Project("dutchy/folio");

        await Assert.That(portfolio.Resolve("sha-one").Value.Id)
            .IsNotEqualTo(portfolio.Resolve("sha-two").Value.Id);
    }

    [Test]
    [MethodDataSource(nameof(ServedFields))]
    public async Task Every_Served_Metadata_Field_Changes_The_Id(string field)
    {
        await Assert.That(Id(Changed(field))).IsNotEqualTo(Id(Metadata()));
    }

    public static IEnumerable<Func<string>> ServedFields() =>
    [
        () => "description", () => "homepage", () => "topics", () => "primary_language",
        () => "stars", () => "forks", () => "license", () => "archived",
        () => "created_at", () => "pushed_at", () => "languages",
        () => "release_tag", () => "release_name", () => "release_url",
        () => "release_prerelease", () => "release_date",
    ];

    private static RepoMetadata Changed(string field) => field switch
    {
        "description" => Metadata() with { Description = "other" },
        "homepage" => Metadata() with { Homepage = "https://other.example" },
        "topics" => Metadata() with { Topics = ["other"] },
        "primary_language" => Metadata() with { PrimaryLanguage = "Go" },
        "stars" => Metadata() with { Stars = 99 },
        "forks" => Metadata() with { Forks = 99 },
        "license" => Metadata() with { License = "Apache-2.0" },
        "archived" => Metadata() with { IsArchived = true },
        "created_at" => Metadata() with { CreatedAt = DateTimeOffset.UnixEpoch.AddDays(1) },
        "pushed_at" => Metadata() with { PushedAt = DateTimeOffset.UnixEpoch.AddDays(1) },
        "languages" => Metadata() with { Languages = [new RepoLanguage("Go", 10)] },
        "release_tag" => WithRelease(Release() with { TagName = "v2" }),
        "release_name" => WithRelease(Release() with { Name = "Renamed" }),
        "release_url" => WithRelease(Release() with { Url = new Uri("https://example.com/other") }),
        "release_prerelease" => WithRelease(Release() with { IsPrerelease = true }),
        "release_date" => WithRelease(Release() with { PublishedAt = DateTimeOffset.UnixEpoch.AddDays(1) }),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static RepoMetadata WithRelease(RepoRelease release) => Metadata() with { Releases = [release] };

    private static RepoRelease Release() => new(
        "v1",
        "Release v1",
        DateTimeOffset.UnixEpoch,
        new Uri("https://example.com/v1"),
        IsPrerelease: false);

    private static RepoMetadata Metadata() => Fixture.Metadata("dutchy/folio") with { Releases = [Release()] };

    private static string Id(RepoMetadata metadata) =>
        Portfolio.Valid().Project("dutchy/folio", metadata: metadata).Resolve().Value.Id;
}
