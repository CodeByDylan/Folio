namespace Folio.Domain.Model;

/// <summary>Derives the paths and names a project's repo-and-path location implies.</summary>
public static class ProjectLocation
{
    /// <summary>Gets the directory a slug is derived from.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="path">The project's path within the repository, empty at its root.</param>
    /// <returns>The last segment of the path, or of the repository name.</returns>
    public static string Directory(string repo, string path) =>
        path.Length == 0 ? repo.Split('/')[^1] : path.Split('/')[^1];

    /// <summary>Gets the name a project's diagnostics are filed under before its slug is known.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="path">The project's path within the repository, empty at its root.</param>
    /// <returns>The derived slug, or the repository when none derives.</returns>
    public static string Identity(string repo, string path) =>
        Slug.TryDerive(Directory(repo, path), out Slug slug) ? slug.Value : repo;

    /// <summary>Gets the path of the project's <c>.folio</c> directory.</summary>
    /// <param name="path">The project's path within the repository, empty at its root.</param>
    /// <returns>The repo-relative <c>.folio</c> path.</returns>
    public static string FolioRoot(string path) => path.Length == 0 ? ".folio" : $"{path}/.folio";

    /// <summary>Gets whether a file is a README sitting directly in the project's own directory.</summary>
    /// <param name="candidate">The repo-relative path to test.</param>
    /// <param name="path">The project's path within the repository, empty at its root.</param>
    /// <returns><see langword="true" /> for the project's own README.</returns>
    public static bool IsReadme(string candidate, string path)
    {
        string prefix = path.Length == 0 ? string.Empty : $"{path}/";

        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            && !candidate[prefix.Length..].Contains('/', StringComparison.Ordinal)
            && candidate[prefix.Length..].StartsWith("README.", StringComparison.OrdinalIgnoreCase);
    }
}
