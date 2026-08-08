namespace Folio.Domain.Content;

/// <summary>Decides whether a link points back at the site, and strips the site's path prefix.</summary>
internal sealed class SitePath(Uri siteUrl)
{
    private readonly string[] _prefix = siteUrl.AbsolutePath
        .Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Gets whether a host differs from the site's only by a <c>www.</c> prefix.</summary>
    /// <param name="candidate">The link to test.</param>
    /// <returns><see langword="true" /> if the hosts differ only by that prefix.</returns>
    public bool IsWwwNearMatch(Uri candidate)
    {
        string mine = siteUrl.Host;
        string theirs = candidate.Host;

        return !string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Strip(mine), Strip(theirs), StringComparison.OrdinalIgnoreCase);

        static string Strip(string host) =>
            host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>Matches a link against the site's origin and path prefix.</summary>
    /// <param name="candidate">The absolute link to test.</param>
    /// <param name="path">The path with the site's prefix removed, always starting with <c>/</c>.</param>
    /// <returns><see langword="true" /> if the link is internal to the site.</returns>
    public bool TryMatch(Uri candidate, out string path)
    {
        path = string.Empty;

        if (!string.Equals(candidate.Scheme, siteUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Host, siteUrl.Host, StringComparison.OrdinalIgnoreCase)
            || candidate.Port != siteUrl.Port)
        {
            return false;
        }

        string[] segments = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < _prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < _prefix.Length; i++)
        {
            if (!string.Equals(segments[i], _prefix[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        path = '/' + string.Join('/', segments.Skip(_prefix.Length)) + candidate.Query + candidate.Fragment;
        return true;
    }
}
