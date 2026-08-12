using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Ingestion.Media;
using Folio.Ingestion.Snapshots;
using Loom.Results;
using Microsoft.Extensions.Logging;

namespace Folio.Ingestion.GitHub;

/// <summary>Which repositories are read from a working tree instead of the capture.</summary>
/// <param name="Directories">Repository roots by <c>owner/name</c> or bare name, matched case-insensitively.</param>
/// <param name="MaxFileBytes">The per-file cap, applied to local files as it is to fetched ones.</param>
public sealed record OverlaySettings(IReadOnlyDictionary<string, string> Directories, int MaxFileBytes);

/// <summary>
/// Replays a recorded input set, laying local working trees over it. Metadata always comes from the
/// capture, because a directory carries none.
/// </summary>
public sealed class ReplayContentSource(
    OverlaySettings settings,
    ILogger<ReplayContentSource> logger) : IGitHubContentSource
{
    /// <inheritdoc />
    public Task<Result<FetchResult>> FetchAsync(StoredInputs? previous, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (previous is null)
        {
            return Task.FromResult<Result<FetchResult>>(FolioIngestionErrors.ContentFault(
                "Replay found no capture. Start once with Content:Mode=GitHub to record one."));
        }

        string age = Age(previous.CapturedAt);
        ReplayLog.Replaying(logger, age, previous.CapturedAt, settings.Directories.Count);

        List<Diagnostic> diagnostics = [];
        StoredInputs replayed = Overlay(previous, diagnostics);

        return Task.FromResult<Result<FetchResult>>(
            new FetchResult(replayed, diagnostics, Requests: 0, BudgetRemaining: int.MaxValue));
    }

    private static string Age(DateTimeOffset capturedAt)
    {
        TimeSpan age = DateTimeOffset.UtcNow - capturedAt;

        return age < TimeSpan.FromHours(1)
            ? $"{(int)age.TotalMinutes}m"
            : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
    }

    private StoredInputs Overlay(StoredInputs captured, List<Diagnostic> diagnostics)
    {
        (FileSet central, IReadOnlySet<string> centralMedia, IReadOnlyDictionary<string, MediaSize> centralSizes) =
            Apply(captured.CentralRepo, string.Empty, captured.Central, captured.CentralMediaPaths,
                captured.CentralMediaSizes, diagnostics);

        List<RepoInput> repos = [];

        foreach (RepoInput repo in captured.Repos)
        {
            (FileSet files, IReadOnlySet<string> media, IReadOnlyDictionary<string, MediaSize> sizes) =
                Apply(repo.Repo, repo.Path, repo.Files, repo.MediaPaths, repo.MediaSizes, diagnostics);

            repos.Add(repo with { Files = files, MediaPaths = media, MediaSizes = sizes });
        }

        return captured with
        {
            Central = central,
            CentralMediaPaths = centralMedia,
            CentralMediaSizes = centralSizes,
            Repos = repos,
        };
    }

    /// <summary>Lays one repository's working tree over its captured contents, if it is mapped.</summary>
    private (FileSet Files, IReadOnlySet<string> Media, IReadOnlyDictionary<string, MediaSize> Sizes) Apply(
        string repo,
        string path,
        FileSet captured,
        IReadOnlySet<string> capturedMedia,
        IReadOnlyDictionary<string, MediaSize> capturedSizes,
        List<Diagnostic> diagnostics)
    {
        if (Mapped(repo) is not { } root)
        {
            return (captured, capturedMedia, capturedSizes);
        }

        string folioRoot = ProjectLocation.FolioRoot(path);

        if (!Directory.Exists(Path.Combine(root, folioRoot)))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.OverlayRootInvalid,
                DiagnosticSeverity.Error,
                $"The overlay for '{repo}' has no '{folioRoot}' at '{root}'; the capture was used instead.",
                ProjectLocation.Identity(repo, path)));

            return (captured, capturedMedia, capturedSizes);
        }

        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);

        // The working tree replaces the whole `.folio` subtree, so a file deleted locally is gone here too.
        foreach (string existing in captured.Paths)
        {
            if (existing.StartsWith(folioRoot + "/", StringComparison.Ordinal))
            {
                continue;
            }

            files[existing] = Read(Path.Combine(root, existing)) is { } replaced
                ? replaced
                : captured.TryGet(existing, out ReadOnlyMemory<byte> kept) ? kept : default;
        }

        foreach (string local in Enumerate(Path.Combine(root, folioRoot)))
        {
            string relative = Path.GetRelativePath(root, local).Replace('\\', '/');

            if (Read(local) is { } contents)
            {
                files[relative] = contents;
            }
        }

        FileSet set = new(files);
        HashSet<string> media = new(StringComparer.Ordinal);
        Dictionary<string, MediaSize> sizes = new(StringComparer.Ordinal);

        foreach (string declared in MediaReferenceReader.Read(set, folioRoot)
            .Concat(MediaReferenceReader.ReadSections(set, folioRoot)))
        {
            if (Read(Path.Combine(root, declared)) is { } image)
            {
                _ = media.Add(declared);

                if (Measure(image.Span) is { } size)
                {
                    sizes[declared] = size;
                }

                continue;
            }

            // Media declared but absent locally may still exist at the captured commit.
            if (capturedMedia.Contains(declared))
            {
                _ = media.Add(declared);

                if (capturedSizes.TryGetValue(declared, out MediaSize? recorded))
                {
                    sizes[declared] = recorded;
                }
            }
        }

        return (set, media, sizes);
    }

    private static MediaSize? Measure(ReadOnlySpan<byte> contents) =>
        ImageHeader.TryRead(contents, out MediaSize size) ? size : null;

    private static IEnumerable<string> Enumerate(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            : [];

    private ReadOnlyMemory<byte>? Read(string file)
    {
        try
        {
            FileInfo info = new(file);

            // Over-size files are dropped, so a declared one surfaces as missing rather than blowing the budget.
            return !info.Exists || info.Length > settings.MaxFileBytes
                ? null
                : File.ReadAllBytes(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Finds a mapped directory by full <c>owner/name</c> or by bare repository name.</summary>
    private string? Mapped(string repo)
    {
        if (settings.Directories.TryGetValue(repo, out string? mapped))
        {
            return mapped;
        }

        string name = repo[(repo.LastIndexOf('/') + 1)..];

        foreach ((string key, string directory) in settings.Directories)
        {
            string candidate = key[(key.LastIndexOf('/') + 1)..];

            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }
}

/// <summary>Says what a replay is serving, so a stale capture is visible rather than assumed fresh.</summary>
internal static partial class ReplayLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Replaying a capture taken {Age} ago ({CapturedAt:u}), with {Overlays} overlay(s).")]
    public static partial void Replaying(ILogger logger, string age, DateTimeOffset capturedAt, int overlays);
}
