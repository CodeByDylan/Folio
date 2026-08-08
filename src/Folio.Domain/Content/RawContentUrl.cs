namespace Folio.Domain.Content;

/// <summary>Addresses a repository file at a pinned commit on GitHub's raw-content host.</summary>
public static class RawContentUrl
{
    /// <summary>The only host media is served from, and the only one it may be probed on.</summary>
    public const string Host = "raw.githubusercontent.com";

    /// <summary>Builds the URL of a repository file at a pinned commit.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="pinnedSha">The commit the URL names, so the content behind it never changes.</param>
    /// <param name="path">The repo-relative path.</param>
    /// <returns>The absolute URL.</returns>
    public static Uri For(string repo, string pinnedSha, string path) =>
        new($"https://{Host}/{repo}/{pinnedSha}/{path}");
}
