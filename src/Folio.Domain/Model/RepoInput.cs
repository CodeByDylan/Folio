namespace Folio.Domain.Model;

/// <summary>The central <c>.folio</c> and the commit it was read from.</summary>
/// <param name="Repo">The repository holding it, as <c>owner/name</c>.</param>
/// <param name="PinnedSha">The commit its files and media were read from.</param>
/// <param name="Files">Its contents.</param>
/// <param name="MediaSizes">Intrinsic sizes by repo-relative path, for media that could be measured.</param>
/// <param name="MediaPaths">The declared media paths that exist at the pinned commit.</param>
public sealed record CentralInput(
    string Repo,
    string PinnedSha,
    FileSet Files,
    IReadOnlyDictionary<string, MediaSize> MediaSizes,
    IReadOnlySet<string> MediaPaths);

/// <summary>Everything the resolver needs about one listed project, assembled before resolution starts.</summary>
/// <param name="Repo">The <c>owner/name</c> entry from <c>projects.toml</c>, already expanded.</param>
/// <param name="Path">The subdirectory containing <c>.folio</c>, empty at the repository root.</param>
/// <param name="PinnedSha">The commit every file and media URL is read from.</param>
/// <param name="Files">The repository's <c>.folio</c> contents, plus the README when one is wanted.</param>
/// <param name="Metadata">The derived GitHub facts.</param>
/// <param name="MediaSizes">Intrinsic sizes by repo-relative path, for media that could be measured.</param>
/// <param name="MediaPaths">The declared media paths that exist at the pinned commit, wherever they live.</param>
public sealed record RepoInput(
    string Repo,
    string Path,
    string PinnedSha,
    FileSet Files,
    RepoMetadata Metadata,
    IReadOnlyDictionary<string, MediaSize> MediaSizes,
    IReadOnlySet<string> MediaPaths);

/// <summary>An image's intrinsic size.</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public sealed record MediaSize(int Width, int Height);
