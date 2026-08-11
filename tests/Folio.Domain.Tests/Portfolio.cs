using System.Text;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Resolution;
using Loom.Results;

namespace Folio.Domain.Tests;

/// <summary>Builds a small in-memory portfolio for one scenario.</summary>
internal sealed class Portfolio
{
    private readonly Dictionary<string, string> _central = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaSize> _centralSizes = new(StringComparer.Ordinal);
    private readonly List<(string Repo, Dictionary<string, string> Files, Dictionary<string, MediaSize> Sizes,
        RepoMetadata? Metadata, IReadOnlySet<string>? MediaPaths)> _repos = [];

    /// <summary>Creates a portfolio with the three central files already valid.</summary>
    /// <param name="locales">The locales to declare.</param>
    /// <param name="defaultLocale">The locale to fall back to.</param>
    /// <returns>The builder.</returns>
    public static Portfolio Valid(string locales = "\"en\"", string defaultLocale = "en")
    {
        Portfolio portfolio = new();

        portfolio._central[".folio/site.toml"] = $"""
            version = 1

            [site]
            url            = "https://dutchy.dev"
            default_locale = "{defaultLocale}"
            locales        = [{locales}]
            owner          = "dutchy"
            """;

        portfolio._central[".folio/projects.toml"] = "version = 1\n";
        portfolio._central[".folio/tags.toml"] = "version = 1\n";

        return portfolio;
    }

    /// <summary>Replaces or adds a central file.</summary>
    /// <param name="path">The path under the central repository.</param>
    /// <param name="contents">The file's text, or <see langword="null" /> to remove it.</param>
    /// <returns>The builder.</returns>
    public Portfolio Central(string path, string? contents)
    {
        if (contents is null)
        {
            _ = _central.Remove(path);
        }
        else
        {
            _central[path] = contents;
        }

        return this;
    }

    /// <summary>Lists a project centrally and supplies its files.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="files">The repository's files, keyed by repo-relative path.</param>
    /// <param name="entry">Extra keys for the <c>projects.toml</c> entry.</param>
    /// <param name="sizes">Media sizes by repo-relative path.</param>
    /// <param name="metadata">GitHub metadata, defaulting to the fixture's.</param>
    /// <param name="mediaPaths">Media present at the pinned commit, defaulting to what the files hold.</param>
    /// <returns>The builder.</returns>
    public Portfolio Project(
        string repo,
        Dictionary<string, string>? files = null,
        string entry = "",
        Dictionary<string, MediaSize>? sizes = null,
        RepoMetadata? metadata = null,
        IReadOnlySet<string>? mediaPaths = null)
    {
        _central[".folio/projects.toml"] += $"\n[[projects]]\nrepo = \"{repo}\"\n{entry}\n";
        _repos.Add((repo, files ?? [], sizes ?? [], metadata, mediaPaths));
        return this;
    }

    /// <summary>Resolves the portfolio and returns its diagnostics.</summary>
    /// <returns>The diagnostics, whether resolution succeeded or failed.</returns>
    public IReadOnlyList<Diagnostic> Diagnostics()
    {
        Result<Snapshot> result = Resolve();

        return result.IsSuccess
            ? result.Value.Diagnostics
            : ((Error<IReadOnlyList<Diagnostic>>)result.Error).Metadata;
    }

    /// <summary>Resolves the portfolio.</summary>
    /// <param name="centralSha">The commit the central files are pinned to.</param>
    /// <returns>The snapshot, or the failure that prevented it.</returns>
    public Result<Snapshot> Resolve(string centralSha = "central-sha") =>
        new PortfolioResolver().Resolve(
            Fixture.Central("dutchy/portfolio", centralSha, Set(_central), _centralSizes),
            [.. _repos.Select(repo =>
            {
                RepoInput input = Fixture.Build(repo.Repo, Set(repo.Files), sizes: repo.Sizes) with
                {
                    Metadata = repo.Metadata ?? Fixture.Metadata(repo.Repo),
                };

                return repo.MediaPaths is null ? input : input with { MediaPaths = repo.MediaPaths };
            })],
            "1.0.0",
            DateTimeOffset.UnixEpoch);

    private static FileSet Set(Dictionary<string, string> files) =>
        new(files.Select(file => new KeyValuePair<string, ReadOnlyMemory<byte>>(
            file.Key,
            Encoding.UTF8.GetBytes(file.Value))));
}
