using System.Text;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Folio.Ingestion.Snapshots;
using Loom.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Folio.Ingestion.Tests;

/// <summary>Replays a capture with a working tree laid over it.</summary>
public sealed class ReplayContentSourceTests
{
    private static readonly RepoMetadata Metadata = new(
        "CodeByDylan",
        "Folio",
        "A portfolio API",
        Homepage: null,
        ["dotnet", "portfolio"],
        "C#",
        [new RepoLanguage("C#", 1000)],
        Stars: 42,
        Forks: 3,
        "MIT",
        IsArchived: false,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        []);

    [Test]
    public async Task Fails_When_Nothing_Has_Been_Recorded()
    {
        Result<FetchResult> result = await Source([]).FetchAsync(previous: null, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Message).Contains("no capture");
    }

    [Test]
    public async Task Returns_The_Capture_Untouched_When_Nothing_Is_Mapped()
    {
        StoredInputs captured = Captured("version = 1\ntitle = \"recorded\"");

        FetchResult replayed = await Replay(Source([]), captured);

        await Assert.That(Text(replayed.Inputs.Central, ".folio/site.toml")).IsEqualTo(
            "version = 1\ntitle = \"recorded\"");
    }

    [Test]
    public async Task Reads_A_Mapped_Working_Tree_Over_The_Capture()
    {
        string root = Tree(("site.toml", "version = 1\ntitle = \"local\""));
        StoredInputs captured = Captured("version = 1\ntitle = \"recorded\"");

        FetchResult replayed = await Replay(Source(new() { ["DotFolio"] = root }), captured);

        await Assert.That(Text(replayed.Inputs.Central, ".folio/site.toml")).IsEqualTo(
            "version = 1\ntitle = \"local\"");
    }

    [Test]
    public async Task Drops_A_File_The_Working_Tree_No_Longer_Has()
    {
        string root = Tree(("site.toml", "version = 1"));

        StoredInputs captured = Captured("version = 1") with
        {
            Central = new FileSet(
            [
                Entry(".folio/site.toml", "version = 1"),
                Entry(".folio/sections/gone.toml", "version = 1"),
            ]),
        };

        FetchResult replayed = await Replay(Source(new() { ["DotFolio"] = root }), captured);

        await Assert.That(replayed.Inputs.Central.Paths).DoesNotContain(".folio/sections/gone.toml");
    }

    [Test]
    public async Task Keeps_Metadata_From_The_Capture()
    {
        string root = Tree(("site.toml", "version = 1"));

        FetchResult replayed = await Replay(
            Source(new() { ["Folio"] = root }), Captured("version = 1"));

        // A directory carries no stars, so an overlay must never be able to change them.
        await Assert.That(replayed.Inputs.Repos[0].Metadata.Stars).IsEqualTo(42);
        await Assert.That(replayed.Inputs.Repos[0].Metadata.Topics).Contains("portfolio");
    }

    [Test]
    public async Task Matches_A_Mapping_By_Full_Owner_And_Name()
    {
        string root = Tree(("site.toml", "version = 1\ntitle = \"local\""));

        FetchResult replayed = await Replay(
            Source(new() { ["CodeByDylan/DotFolio"] = root }), Captured("version = 1"));

        await Assert.That(Text(replayed.Inputs.Central, ".folio/site.toml")).Contains("local");
    }

    [Test]
    public async Task Falls_Back_To_The_Capture_When_An_Overlay_Holds_No_Folio()
    {
        string empty = Directory.CreateTempSubdirectory("folio-empty").FullName;

        FetchResult replayed = await Replay(
            Source(new() { ["DotFolio"] = empty }), Captured("version = 1\ntitle = \"recorded\""));

        await Assert.That(Text(replayed.Inputs.Central, ".folio/site.toml")).Contains("recorded");

        await Assert.That(replayed.Diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.OverlayRootInvalid);
    }

    [Test]
    public async Task Reports_No_Requests_Against_The_Rate_Limit()
    {
        FetchResult replayed = await Replay(Source([]), Captured("version = 1"));

        await Assert.That(replayed.Requests).IsEqualTo(0);
    }

    private static ReplayContentSource Source(Dictionary<string, string> overlays) =>
        new(new OverlaySettings(overlays, MaxFileBytes: 5 * 1024 * 1024), NullLogger<ReplayContentSource>.Instance);

    private static async Task<FetchResult> Replay(ReplayContentSource source, StoredInputs captured)
    {
        Result<FetchResult> result = await source.FetchAsync(captured, CancellationToken.None);

        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Message);
    }

    private static KeyValuePair<string, ReadOnlyMemory<byte>> Entry(string path, string contents) =>
        new(path, Encoding.UTF8.GetBytes(contents));

    private static string Text(FileSet files, string path) =>
        files.TryGet(path, out ReadOnlyMemory<byte> contents)
            ? Encoding.UTF8.GetString(contents.Span)
            : throw new InvalidOperationException($"'{path}' is not in the set.");

    /// <summary>Writes a throwaway repository root with a <c>.folio</c> in it.</summary>
    private static string Tree(params (string Path, string Contents)[] files)
    {
        string root = Directory.CreateTempSubdirectory("folio-replay").FullName;

        foreach ((string path, string contents) in files)
        {
            string full = Path.Combine(root, ".folio", path);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }

        return root;
    }

    private static StoredInputs Captured(string site) =>
        new(
            "CodeByDylan/DotFolio",
            new string('a', 40),
            new FileSet([Entry(".folio/site.toml", site)]),
            new Dictionary<string, MediaSize>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            [
                new RepoInput(
                    "CodeByDylan/Folio",
                    string.Empty,
                    new string('b', 40),
                    new FileSet([Entry(".folio/project.toml", "version = 1")]),
                    Metadata,
                    new Dictionary<string, MediaSize>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal)),
            ],
            DateTimeOffset.UtcNow);
}
