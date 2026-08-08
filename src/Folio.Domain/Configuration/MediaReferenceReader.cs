using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Configuration;

/// <summary>Reads the media a project's configuration names, so ingestion measures only those.</summary>
public static class MediaReferenceReader
{
    /// <summary>Reads the repo-relative paths of media declared in <c>project.toml</c>.</summary>
    /// <param name="files">The repository's file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <returns>The declared paths that resolve inside the repository, in role order.</returns>
    public static IReadOnlyList<string> Read(FileSet files, string folioRoot)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (!new ProjectConfigParser().TryParse(files, folioRoot, new DiagnosticSink(), out ProjectConfig config))
        {
            return [];
        }

        string projectPath = folioRoot.EndsWith("/.folio", StringComparison.Ordinal)
            ? folioRoot[..^"/.folio".Length]
            : string.Empty;

        List<string> paths = [];

        foreach ((string _, string reference) in config.Media.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (Content.LinkTarget.IsAbsolute(reference, out _))
            {
                continue;
            }

            string? path = Resolve(reference, projectPath);

            if (path is not null)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>Resolves a media reference the way markdown paths resolve.</summary>
    /// <param name="reference">The reference as authored.</param>
    /// <param name="projectPath">The project's path within the repository, empty at its root.</param>
    /// <returns>The repository path, or <see langword="null" /> if it escapes the repository.</returns>
    public static string? Resolve(string reference, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // Root-absolute references resolve from the repository root, even in a monorepo.
        return reference.StartsWith('/') || projectPath.Length == 0
            ? Content.RepoPath.Resolve('/' + reference.TrimStart('/'), string.Empty)
            : Content.RepoPath.Resolve($"/{projectPath}/{reference}", string.Empty);
    }
}
