using System.Collections.Concurrent;
using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Ingestion.Media;
using Folio.Ingestion.Snapshots;
using Loom.Results;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Folio.Ingestion.GitHub;

/// <summary>How a fetch behaves.</summary>
/// <param name="CentralRepository">The repository holding the central <c>.folio</c>, as <c>owner/name</c>.</param>
/// <param name="CentralRef">The branch, tag or SHA to read the central config from.</param>
/// <param name="FetchConcurrency">How many requests may be in flight at once.</param>
/// <param name="MinimumRateLimitBudget">The remaining budget below which a fetch will not start.</param>
/// <param name="MaxFileBytes">The largest single file that will be fetched.</param>
/// <param name="MaxFileCount">The most files that will be fetched from one repository.</param>
/// <param name="MaxTotalBytes">The most bytes that will be fetched from one repository.</param>
public sealed record FetchSettings(
    string CentralRepository,
    string? CentralRef,
    int FetchConcurrency,
    int MinimumRateLimitBudget,
    int MaxFileBytes,
    int MaxFileCount,
    long MaxTotalBytes);

/// <summary>An assembled input set and what assembling it found.</summary>
/// <param name="Inputs">The inputs a build can run over.</param>
/// <param name="Diagnostics">Content faults met while fetching.</param>
/// <param name="Requests">How many GitHub requests the fetch issued.</param>
/// <param name="BudgetRemaining">Requests left in the rate-limit window when the fetch began.</param>
public sealed record FetchResult(
    StoredInputs Inputs,
    IReadOnlyList<Diagnostic> Diagnostics,
    int Requests,
    int BudgetRemaining);

/// <summary>Assembles a build's inputs from GitHub.</summary>
public sealed class GitHubContentSource(
    IGitHubClient client,
    IMediaProbe media,
    FetchSettings settings,
    ILogger<GitHubContentSource> logger) : IGitHubContentSource
{
    private const int ReleasePageSize = 20;

    private sealed class CallCount
    {
        private int _issued;

        public int Issued => Volatile.Read(ref _issued);

        public void Increment() => Interlocked.Increment(ref _issued);
    }

    /// <inheritdoc />
    public async Task<Result<FetchResult>> FetchAsync(StoredInputs? previous, CancellationToken cancellationToken)
    {
        DiagnosticSink sink = new();
        CallCount calls = new();

        Result<int> budget = await CheckBudgetAsync(calls, cancellationToken);

        if (budget.IsFailure)
        {
            return budget.Error;
        }

        Result<RepoContents> central = await ReadAsync(
            settings.CentralRepository, settings.CentralRef, string.Empty, previous, calls, cancellationToken);

        if (central.IsFailure)
        {
            // A central repository that cannot be read is a configuration fault, not a blip.
            return central.Error.Code switch
            {
                FolioIngestionErrors.ContentFaultCode =>
                    FolioIngestionErrors.CentralUnreadable(settings.CentralRepository, central.Error.Message),
                FolioIngestionErrors.TreeTruncatedCode =>
                    FolioIngestionErrors.CentralUnparseable(settings.CentralRepository, central.Error.Message),
                _ => central.Error,
            };
        }

        if (!ProjectListReader.TryRead(central.Value.Files, out IReadOnlyList<ProjectReference> references))
        {
            return FolioIngestionErrors.CentralUnparseable(
                settings.CentralRepository,
                "its configuration could not be parsed.");
        }

        ConcurrentBag<(int Index, RepoInput Input)> fetched = [];
        List<(int Index, string Repo, string Code, string Message)> faults = [];
        List<(int Index, Error Error)> transient = [];
        using CancellationTokenSource abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel.ForEachAsync(
            references.Select((reference, index) => (reference, index)),
            new ParallelOptions { MaxDegreeOfParallelism = settings.FetchConcurrency, CancellationToken = abandon.Token },
            async (pair, token) =>
            {
                Result<RepoContents> contents = await ReadAsync(
                        pair.reference.Repo,
                        pair.reference.Ref,
                        pair.reference.Path,
                        previous,
                        calls,
                        token,
                        pair.reference.UseReadme);

                if (contents.IsFailure)
                {
                    string? code = contents.Error.Code switch
                    {
                        FolioIngestionErrors.ContentFaultCode => DiagnosticCodes.ProjectNotFound,
                        FolioIngestionErrors.TreeTruncatedCode => DiagnosticCodes.ProjectTreeTruncated,
                        _ => null,
                    };

                    if (code is not null)
                    {
                        lock (faults)
                        {
                            faults.Add((pair.index, ProjectLocation.Identity(pair.reference.Repo, pair.reference.Path), code, contents.Error.Message));
                        }

                        return;
                    }

                    lock (transient)
                    {
                        transient.Add((pair.index, contents.Error));
                    }

                    await abandon.CancelAsync();
                    return;
                }

                try
                {
                    fetched.Add((pair.index, await ToInputAsync(pair.reference, contents.Value, calls, token)));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Metadata for a repo whose tree already listed; a fault here says nothing about it.
                    IngestionLog.Transient(logger, exception);
                    lock (transient)
                    {
                        transient.Add((pair.index, FolioIngestionErrors.Transient(exception)));
                    }

                    await abandon.CancelAsync();
                }
            });
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Anything cancelling that is not the abandon signal is a timeout.
            if (!abandon.IsCancellationRequested)
            {
                return FolioIngestionErrors.Transient(exception);
            }
        }

        if (transient.Count > 0)
        {
            return transient.OrderBy(fault => fault.Index).First().Error;
        }

        IngestionLog.Fetched(logger, fetched.Count, references.Count);
        foreach ((_, string repo, string code, string message) in faults.OrderBy(fault => fault.Index))
        {
            sink.ForProject(repo).Error(code, message);
        }

        return new FetchResult(
            new StoredInputs(
                settings.CentralRepository,
                central.Value.Sha,
                central.Value.Files,
                await MeasureAsync(
                    settings.CentralRepository,
                    central.Value.Sha,
                    central.Value.MediaPresent,
                    cancellationToken),
                central.Value.MediaPresent,
                [.. fetched.OrderBy(entry => entry.Index).Select(entry => entry.Input)],
                DateTimeOffset.UtcNow),
            sink.Diagnostics,
            calls.Issued,
            budget.Value);
    }

    private async Task<Result<int>> CheckBudgetAsync(CallCount calls, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Increment();
            MiscellaneousRateLimit limits = await client.RateLimit.GetRateLimits();
            int remaining = limits.Resources.Core.Remaining;

            return remaining < settings.MinimumRateLimitBudget
                ? FolioIngestionErrors.RateLimitInsufficient(remaining, settings.MinimumRateLimitBudget)
                : remaining;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            IngestionLog.Transient(logger, exception);
            return FolioIngestionErrors.Transient(exception);
        }
    }

    private async Task<Result<RepoContents>> ReadAsync(
        string repo,
        string? reference,
        string path,
        StoredInputs? previous,
        CallCount calls,
        CancellationToken cancellationToken,
        bool includeReadme = false)
    {
        string[] parts = repo.Split('/');

        if (parts.Length != 2)
        {
            return FolioIngestionErrors.ContentFault($"'{repo}' is not a valid owner/name pair.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Increment();
            Repository repository = await client.Repository.Get(parts[0], parts[1]);
            string target = reference ?? repository.DefaultBranch;

            cancellationToken.ThrowIfCancellationRequested();
            calls.Increment();
            TreeResponse tree = await client.Git.Tree.GetRecursive(parts[0], parts[1], target);

            if (tree.Truncated)
            {
                return FolioIngestionErrors.TreeTruncated(repo);
            }

            // A .folio directory is optional, but the path itself must exist or the entry is a typo.
            if (path.Length > 0
                && !tree.Tree.Any(item => item.Path.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                return FolioIngestionErrors.ContentFault(
                    $"'{repo}' has no '{path}' directory at the pinned commit.");
            }

            string folioRoot = ProjectLocation.FolioRoot(path);

            List<TreeItem> matched =
            [
                .. tree.Tree.Where(item =>
                    item.Type.Value == TreeType.Blob
                    && (item.Path.StartsWith(folioRoot + "/", StringComparison.Ordinal)
                        || (includeReadme && ProjectLocation.IsReadme(item.Path, path)))),
            ];

            // Repository content is third-party, so a file is measured against the caps before it is pulled.
            List<TreeItem> wanted = [];
            long total = 0;

            foreach (TreeItem item in matched.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                // Over-size files are dropped from the set; a declared one then surfaces as media/content missing.
                if (item.Size > settings.MaxFileBytes)
                {
                    continue;
                }

                if (wanted.Count >= settings.MaxFileCount || total + item.Size > settings.MaxTotalBytes)
                {
                    return FolioIngestionErrors.ContentFault(
                        $"'{repo}' exceeds the fetch budget ({settings.MaxFileCount} files / {settings.MaxTotalBytes} bytes).");
                }

                wanted.Add(item);
                total += item.Size;
            }

            Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);

            foreach (TreeItem item in wanted)
            {
                files[item.Path] = await BlobAsync(parts[0], parts[1], item, previous, calls, cancellationToken);
            }

            FileSet set = new(files);

            // Declared media may live anywhere in the repository, so existence comes from the tree.
            HashSet<string> present = new(StringComparer.Ordinal);

            foreach (string declared in MediaReferenceReader.Read(set, folioRoot)
                .Concat(MediaReferenceReader.ReadSections(set, folioRoot)))
            {
                if (tree.Tree.Any(item => item.Type.Value == TreeType.Blob
                    && string.Equals(item.Path, declared, StringComparison.Ordinal)))
                {
                    _ = present.Add(declared);
                }
            }

            return new RepoContents(tree.Sha, set, repository, present);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (GitHubFault.Classify(exception) is GitHubFaultKind.Content)
            {
                return FolioIngestionErrors.ContentFault($"'{repo}' could not be read: {exception.Message}");
            }

            IngestionLog.Transient(logger, exception);
            return FolioIngestionErrors.Transient(exception);
        }
    }

    private async Task<ReadOnlyMemory<byte>> BlobAsync(
        string owner,
        string name,
        TreeItem item,
        StoredInputs? previous,
        CallCount calls,
        CancellationToken cancellationToken)
    {
        if (Cached(previous).TryGetValue(item.Sha, out ReadOnlyMemory<byte> hit))
        {
            return hit;
        }

        cancellationToken.ThrowIfCancellationRequested();
        calls.Increment();
        Blob blob = await client.Git.Blob.Get(owner, name, item.Sha);

        return blob.Encoding.Value == EncodingType.Base64
            ? Convert.FromBase64String(blob.Content)
            : System.Text.Encoding.UTF8.GetBytes(blob.Content);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StoredInputs, Dictionary<string, ReadOnlyMemory<byte>>> BlobIndex = new();

    private static Dictionary<string, ReadOnlyMemory<byte>> Cached(StoredInputs? previous)
    {
        if (previous is null)
        {
            return [];
        }

        return BlobIndex.GetValue(previous, inputs =>
        {
            Dictionary<string, ReadOnlyMemory<byte>> index = new(StringComparer.Ordinal);

            foreach (FileSet files in inputs.Repos.Select(repo => repo.Files).Append(inputs.Central))
            {
                foreach (string path in files.Paths)
                {
                    if (files.TryGet(path, out ReadOnlyMemory<byte> contents))
                    {
                        index[ShaOf(contents)] = contents;
                    }
                }
            }

            return index;
        });
    }

    private async Task<RepoInput> ToInputAsync(
        ProjectReference reference,
        RepoContents contents,
        CallCount calls,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RepoLanguage> languages = await LanguagesAsync(contents.Repository, calls, cancellationToken);
        IReadOnlyList<RepoRelease> releases = await ReleasesAsync(contents.Repository, calls, cancellationToken);

        IReadOnlyDictionary<string, MediaSize> sizes = await MeasureAsync(
            reference.Repo, contents.Sha, contents.MediaPresent, cancellationToken);

        return new RepoInput(
            reference.Repo,
            reference.Path,
            contents.Sha,
            contents.Files,
            new RepoMetadata(
                contents.Repository.Owner.Login,
                contents.Repository.Name,
                contents.Repository.Description,
                contents.Repository.Homepage,
                [.. (contents.Repository.Topics ?? []).Order(StringComparer.Ordinal)],
                contents.Repository.Language,
                languages,
                contents.Repository.StargazersCount,
                contents.Repository.ForksCount,
                contents.Repository.License?.SpdxId,
                contents.Repository.Archived,
                contents.Repository.CreatedAt,
                contents.Repository.PushedAt ?? contents.Repository.CreatedAt,
                releases),
            sizes,
            contents.MediaPresent);
    }

    /// <summary>Measures declared media that exists; only those can carry dimensions.</summary>
    private async Task<IReadOnlyDictionary<string, MediaSize>> MeasureAsync(
        string repo,
        string sha,
        IReadOnlySet<string> present,
        CancellationToken cancellationToken)
    {
        Dictionary<string, MediaSize> sizes = new(StringComparer.Ordinal);

        foreach (string path in present.Order(StringComparer.Ordinal))
        {
            if (await media.MeasureAsync(repo, sha, path, cancellationToken) is { } size)
            {
                sizes[path] = size;
            }
        }

        return sizes;
    }

    private async Task<IReadOnlyList<RepoRelease>> ReleasesAsync(
        Repository repository,
        CallCount calls,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Increment();
        IReadOnlyList<Release> releases = await client.Repository.Release.GetAll(
            repository.Owner.Login,
            repository.Name,
            new ApiOptions { PageSize = ReleasePageSize * 2, PageCount = 1 });

        return
        [
            .. releases
                .Where(release => !release.Draft && release.PublishedAt is not null)
                .Select(release => new RepoRelease(
                    release.TagName,
                    string.IsNullOrWhiteSpace(release.Name) ? null : release.Name,
                    release.PublishedAt!.Value,
                    new Uri(release.HtmlUrl),
                    release.Prerelease))
                .OrderByDescending(release => release.PublishedAt)
                .ThenBy(release => release.TagName, StringComparer.Ordinal)
                .Take(ReleasePageSize),
        ];
    }

    private async Task<IReadOnlyList<RepoLanguage>> LanguagesAsync(
        Repository repository,
        CallCount calls,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Increment();
        IReadOnlyList<RepositoryLanguage> languages =
            await client.Repository.GetAllLanguages(repository.Owner.Login, repository.Name);

        return
        [
            .. languages
                .OrderByDescending(language => language.NumberOfBytes)
                .ThenBy(language => language.Name, StringComparer.Ordinal)
                .Select(language => new RepoLanguage(language.Name, language.NumberOfBytes)),
        ];
    }

    private static string ShaOf(ReadOnlyMemory<byte> contents)
    {
        // Git addresses a blob as sha1("blob <length>\0" + contents).
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"blob {contents.Length}\0");
        byte[] buffer = new byte[header.Length + contents.Length];

        header.CopyTo(buffer, 0);
        contents.Span.CopyTo(buffer.AsSpan(header.Length));

        // A git blob is addressed by SHA-1; this must match git, so the algorithm is not a choice.
#pragma warning disable CA5350
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(buffer));
#pragma warning restore CA5350
    }

    private sealed record RepoContents(
        string Sha,
        FileSet Files,
        Repository Repository,
        IReadOnlySet<string> MediaPresent);
}

internal static partial class IngestionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched {Fetched} of {Listed} listed projects.")]
    public static partial void Fetched(ILogger logger, int fetched, int listed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A transient fault abandoned the fetch.")]
    public static partial void Transient(ILogger logger, Exception exception);
}
