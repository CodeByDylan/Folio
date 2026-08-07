namespace Folio.Domain.Content;

/// <summary>Resolves markdown link targets the way github.com does.</summary>
internal static class RepoPath
{
    /// <summary>Resolves a link target against the file containing it.</summary>
    /// <param name="target">A relative or root-absolute path.</param>
    /// <param name="containingFile">The repo-relative path of the file holding the link.</param>
    /// <returns>The repo-relative path, or <see langword="null" /> if it escapes the repository.</returns>
    public static string? Resolve(string target, string containingFile)
    {
        List<string> segments = [];

        if (!target.StartsWith('/'))
        {
            int lastSlash = containingFile.LastIndexOf('/');

            if (lastSlash > 0)
            {
                segments.AddRange(containingFile[..lastSlash].Split('/', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        foreach (string raw in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // Decode first, or %2e%2e survives here and Uri collapses it into a traversal later.
            string segment = Uri.UnescapeDataString(raw);

            if (segment.Contains('/', StringComparison.Ordinal) || segment.Contains('\\', StringComparison.Ordinal))
            {
                return null;
            }

            switch (segment)
            {
                case ".":
                    continue;
                case ".." when segments.Count == 0:
                    return null;
                case "..":
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                default:
                    segments.Add(segment);
                    continue;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
