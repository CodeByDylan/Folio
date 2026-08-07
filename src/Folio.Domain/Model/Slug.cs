using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Folio.Domain.Model;

/// <summary>A project's stable, non-localized identity: lowercase letters, digits and hyphens.</summary>
public readonly record struct Slug
{
    private Slug(string value) => Value = value;

    /// <summary>Gets the slug.</summary>
    public string Value { get; }

    /// <summary>Parses an authored slug, refusing anything outside the permitted set.</summary>
    /// <param name="value">A candidate slug.</param>
    /// <param name="slug">The parsed slug.</param>
    /// <returns><see langword="true" /> if <paramref name="value" /> is a well-formed slug.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out Slug slug)
    {
        slug = default;

        if (string.IsNullOrEmpty(value) || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsPermitted(character))
            {
                return false;
            }
        }

        slug = new Slug(value);
        return true;
    }

    /// <summary>Derives a slug from the directory a project's <c>.folio</c> sits in.</summary>
    /// <param name="directory">The directory name, usually a repository name.</param>
    /// <param name="slug">The derived slug.</param>
    /// <returns><see langword="true" /> if anything usable remained after normalizing.</returns>
    public static bool TryDerive(string? directory, out Slug slug)
    {
        slug = default;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        StringBuilder derived = new(directory.Length);

        foreach (char character in directory)
        {
            char lowered = char.ToLowerInvariant(character);

            if (IsPermitted(lowered) && lowered != '-')
            {
                _ = derived.Append(lowered);
            }
            else if (derived.Length > 0 && derived[^1] != '-')
            {
                _ = derived.Append('-');
            }
        }

        while (derived.Length > 0 && derived[^1] == '-')
        {
            _ = derived.Remove(derived.Length - 1, 1);
        }

        return TryParse(derived.ToString(), out slug);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsPermitted(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
}
