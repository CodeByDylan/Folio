using System.Text;
using Folio.Domain.Model;
using Folio.Ingestion.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace Folio.Ingestion.Tests;

public sealed class SnapshotStoreTests
{
    [Test]
    public async Task Inputs_Round_Trip_Through_Json()
    {
        StoredInputs original = Inputs();

        StoredInputs? restored = StoredInputsSerializer.Deserialize(StoredInputsSerializer.Serialize(original));

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.CentralSha).IsEqualTo("central-sha");
        await Assert.That(restored.Repos).Count().IsEqualTo(1);
        await Assert.That(restored.Repos[0].PinnedSha).IsEqualTo("abc123");
    }

    [Test]
    public async Task File_Contents_Survive_Byte_For_Byte()
    {
        StoredInputs? restored = StoredInputsSerializer.Deserialize(StoredInputsSerializer.Serialize(Inputs()));

        bool found = restored!.Repos[0].Files.TryGet(".folio/project.toml", out ReadOnlyMemory<byte> contents);

        await Assert.That(found).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(contents.Span)).IsEqualTo("version = 1\n");
    }

    [Test]
    public async Task Media_Sizes_Survive()
    {
        StoredInputs? restored = StoredInputsSerializer.Deserialize(StoredInputsSerializer.Serialize(Inputs()));

        await Assert.That(restored!.Repos[0].MediaSizes[".folio/media/hero.png"].Width).IsEqualTo(1280);
    }

    [Test]
    public async Task Unreadable_Bytes_Deserialize_To_Nothing_Rather_Than_Throwing()
    {
        await Assert.That(StoredInputsSerializer.Deserialize("{ not json"u8)).IsNull();
    }

    [Test]
    public async Task The_File_Store_Round_Trips()
    {
        string directory = Directory.CreateTempSubdirectory("folio").FullName;

        try
        {
            FileSnapshotStore store = new(Path.Combine(directory, "inputs.json"), NullLogger<FileSnapshotStore>.Instance);

            await store.WriteAsync(Inputs(), CancellationToken.None);
            StoredInputs? restored = await store.ReadAsync(CancellationToken.None);

            await Assert.That(restored).IsNotNull();
            await Assert.That(restored!.Repos[0].Repo).IsEqualTo("dutchy/folio");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task The_File_Store_Reports_Nothing_When_No_File_Exists()
    {
        FileSnapshotStore store = new(
            Path.Combine(Path.GetTempPath(), $"folio-absent-{Guid.NewGuid():N}.json"),
            NullLogger<FileSnapshotStore>.Instance);

        await Assert.That(await store.ReadAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task The_File_Store_Leaves_No_Temporary_File_Behind()
    {
        string directory = Directory.CreateTempSubdirectory("folio").FullName;

        try
        {
            string path = Path.Combine(directory, "inputs.json");
            await new FileSnapshotStore(path, NullLogger<FileSnapshotStore>.Instance)
                .WriteAsync(Inputs(), CancellationToken.None);

            await Assert.That(Directory.GetFiles(directory).Select(file => Path.GetFileName(file)!)).IsEquivalentTo(["inputs.json"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task An_Unwritable_Path_Is_Logged_Rather_Than_Thrown()
    {
        string directory = Directory.CreateTempSubdirectory("folio").FullName;

        try
        {
            // A file where the store wants a directory: creating the parent fails, not the write.
            string blocker = Path.Combine(directory, "blocked");
            await File.WriteAllTextAsync(blocker, "not a directory");

            FileSnapshotStore store = new(
                Path.Combine(blocker, "inputs.json"),
                NullLogger<FileSnapshotStore>.Instance);

            await store.WriteAsync(Inputs(), CancellationToken.None);

            await Assert.That(await store.ReadAsync(CancellationToken.None)).IsNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task A_Cancelled_Write_Still_Cancels()
    {
        string directory = Directory.CreateTempSubdirectory("folio").FullName;

        try
        {
            FileSnapshotStore store = new(
                Path.Combine(directory, "inputs.json"),
                NullLogger<FileSnapshotStore>.Instance);

            await Assert.That(async () => await store.WriteAsync(Inputs(), new CancellationToken(canceled: true)))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static StoredInputs Inputs() => new(
        "dutchy/portfolio",
        "central-sha",
        new FileSet([new(".folio/site.toml", Encoding.UTF8.GetBytes("version = 1\n"))]),
        [
            new RepoInput(
                "dutchy/folio",
                Path: string.Empty,
                PinnedSha: "abc123",
                Files: new FileSet([new(".folio/project.toml", Encoding.UTF8.GetBytes("version = 1\n"))]),
                Metadata: new RepoMetadata(
                    "dutchy", "folio", "A repo", null, ["cli"], "Rust",
                    [new RepoLanguage("Rust", 10)],
                    1, 2, "MIT", false,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []),
                MediaSizes: new Dictionary<string, MediaSize>(StringComparer.Ordinal)
                {
                    [".folio/media/hero.png"] = new(1280, 720),
                },
                MediaPaths: new HashSet<string>(StringComparer.Ordinal) { ".folio/media/hero.png" }),
        ],
        DateTimeOffset.UnixEpoch);
}
