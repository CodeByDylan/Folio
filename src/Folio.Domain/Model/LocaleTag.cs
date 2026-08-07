namespace Folio.Domain.Model;

/// <summary>A BCP-47 language tag in canonical form: lowercase language, titlecase script, uppercase region.</summary>
public readonly record struct LocaleTag
{
    private LocaleTag(string value) => Value = value;

    /// <summary>Gets the canonical tag.</summary>
    public string Value { get; }

    /// <summary>Parses a tag, canonicalizing its subtags.</summary>
    /// <param name="value">A BCP-47 tag such as <c>en</c>, <c>nl-BE</c> or <c>zh-Hant-TW</c>.</param>
    /// <param name="tag">The canonicalized tag.</param>
    /// <returns><see langword="true" /> if <paramref name="value" /> is well formed.</returns>
    public static bool TryParse(string? value, out LocaleTag tag)
    {
        tag = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] subtags = value.Trim().Split('-');

        if (!IsLanguage(subtags[0]))
        {
            return false;
        }

        for (int i = 1; i < subtags.Length; i++)
        {
            if (!IsSubtag(subtags[i]))
            {
                return false;
            }
        }

        tag = new LocaleTag(string.Join('-', subtags.Select(Canonicalize)));
        return true;
    }

    /// <summary>Removes the rightmost subtag, giving the next less specific locale.</summary>
    /// <param name="parent">The less specific locale.</param>
    /// <returns><see langword="true" /> if a less specific locale exists.</returns>
    public bool TryTruncate(out LocaleTag parent)
    {
        int last = Value.LastIndexOf('-');

        if (last < 0)
        {
            parent = default;
            return false;
        }

        parent = new LocaleTag(Value[..last]);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsLanguage(string subtag) =>
        subtag.Length is >= 2 and <= 8 && subtag.All(char.IsAsciiLetter);

    // BCP-47 subtags are alphanumeric; anything else must not reach the ETag or a header value.
    private static bool IsSubtag(string subtag) =>
        subtag.Length is >= 1 and <= 8 && subtag.All(char.IsAsciiLetterOrDigit);

    private static string Canonicalize(string subtag, int index)
    {
        if (index == 0)
        {
            return subtag.ToLowerInvariant();
        }

        if (subtag.Length == 4 && subtag.All(char.IsAsciiLetter))
        {
            return string.Concat(char.ToUpperInvariant(subtag[0]), subtag[1..].ToLowerInvariant());
        }

        bool isRegion = (subtag.Length == 2 && subtag.All(char.IsAsciiLetter))
            || (subtag.Length == 3 && subtag.All(char.IsAsciiDigit));

        return isRegion ? subtag.ToUpperInvariant() : subtag.ToLowerInvariant();
    }
}
