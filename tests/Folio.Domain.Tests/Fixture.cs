using Folio.Domain.Configuration;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

/// <summary>Loads a committed fixture directory into the shapes the resolver takes.</summary>
internal static class Fixture
{
    /// <summary>Reads a directory tree into a file set, keyed by path relative to it.</summary>
    /// <param name="directory">The directory to read.</param>
    /// <returns>The file set.</returns>
    public static FileSet Load(string directory)
    {
        string root = Path.GetFullPath(directory);

        return new FileSet(Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new KeyValuePair<string, ReadOnlyMemory<byte>>(
                Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllBytes(file))));
    }

    /// <summary>Gets the path of a named fixture.</summary>
    /// <param name="name">The fixture directory name.</param>
    /// <returns>The absolute path.</returns>
    public static string Path_(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>Builds a central input, deriving present media the way ingestion does.</summary>
    /// <param name="repo">The repository holding the central config, as <c>owner/name</c>.</param>
    /// <param name="sha">The commit it was read from.</param>
    /// <param name="files">Its contents.</param>
    /// <param name="sizes">Intrinsic sizes for media that could be measured.</param>
    /// <returns>The input.</returns>
    public static CentralInput Central(
        string repo,
        string sha,
        FileSet files,
        IReadOnlyDictionary<string, MediaSize>? sizes = null)
    {
        HashSet<string> present = new(
            MediaReferenceReader.ReadSections(files, ".folio").Where(files.Paths.Contains),
            StringComparer.Ordinal);

        return new CentralInput(
            repo,
            sha,
            files,
            sizes ?? new Dictionary<string, MediaSize>(StringComparer.Ordinal),
            present);
    }

    /// <summary>Builds a repository input with plausible GitHub metadata.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="directory">The fixture directory holding the repository.</param>
    /// <param name="sizes">Media sizes by repo-relative path.</param>
    /// <returns>The input.</returns>
    public static RepoInput Repo(
        string repo,
        string directory,
        IReadOnlyDictionary<string, MediaSize>? sizes = null,
        string path = "") =>
        Build(repo, Load(directory), path, sizes);

    /// <summary>Builds a repository input from an in-memory file set.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="files">The repository files.</param>
    /// <param name="path">The project's path within the repository.</param>
    /// <param name="sizes">Media sizes by repo-relative path.</param>
    /// <returns>The input.</returns>
    public static RepoInput Build(
        string repo,
        FileSet files,
        string path = "",
        IReadOnlyDictionary<string, MediaSize>? sizes = null) =>
        new(repo,
            Path: path,
            PinnedSha: "abc123",
            Files: files,
            Metadata: Metadata(repo),
            MediaSizes: sizes ?? new Dictionary<string, MediaSize>(StringComparer.Ordinal),
            MediaPaths: MediaIn(files, path));

    // Ingestion checks the tree; in a fixture the file set is the whole repository.
    private static HashSet<string> MediaIn(FileSet files, string path) =>
        [.. MediaReferenceReader.Read(files, ProjectLocation.FolioRoot(path))
            .Where(declared => files.TryGet(declared, out _))];

    /// <summary>Builds GitHub metadata for a repository.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="archived">Whether GitHub reports it archived.</param>
    /// <returns>The metadata.</returns>
    public static RepoMetadata Metadata(string repo, bool archived = false)
    {
        string[] parts = repo.Split('/');

        return new RepoMetadata(
            parts[0],
            parts[1],
            Description: "A repository.",
            Homepage: null,
            Topics: ["cli", "rust"],
            PrimaryLanguage: "Rust",
            // A breakdown whose shares need rounding, so the golden file pins the rule.
            Languages: [new RepoLanguage("Rust", 900), new RepoLanguage("Shell", 100), new RepoLanguage("Markdown", 33)],
            Stars: 12,
            Forks: 3,
            License: "MIT",
            IsArchived: archived,
            CreatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PushedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Releases: []);
    }
}
