namespace Folio.Domain.Content;

/// <summary>Classifies a link target by its syntax rather than by what a URI parser will accept.</summary>
internal static class LinkTarget
{
    /// <summary>Gets whether a target carries an RFC 3986 scheme.</summary>
    /// <param name="target">The raw link target.</param>
    /// <param name="absolute">The parsed URL.</param>
    /// <returns><see langword="true" /> if the target names a scheme and parses.</returns>
    public static bool IsAbsolute(string target, out Uri absolute)
    {
        absolute = null!;
        int colon = target.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0)
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            char character = target[i];

            bool valid = i == 0
                ? char.IsAsciiLetter(character)
                : char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.';

            if (!valid)
            {
                return false;
            }
        }

        return Uri.TryCreate(target, UriKind.Absolute, out absolute!);
    }

    /// <summary>Gets whether a target is an absolute <c>http</c> or <c>https</c> URL.</summary>
    /// <param name="target">The raw link target.</param>
    /// <param name="url">The parsed URL.</param>
    /// <returns><see langword="true" /> if the target is a web URL.</returns>
    public static bool IsWebUrl(string? target, out Uri url)
    {
        url = null!;

        return target is not null
            && IsAbsolute(target, out url)
            && url.Scheme is "http" or "https";
    }
}
