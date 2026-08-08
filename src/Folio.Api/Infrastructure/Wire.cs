using System.Text;

namespace Folio.Api.Infrastructure;

/// <summary>Conversions every wire shape needs, so no aggregate owns them.</summary>
internal static class Wire
{
    /// <summary>Converts an enum member to the lowercase form used on the wire.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="value">The member to convert.</param>
    /// <returns>The lowercase name.</returns>
    public static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    /// <summary>Converts a pascal-case enum name to the hyphenated form used on the wire.</summary>
    /// <param name="name">The enum member name.</param>
    /// <returns>The hyphenated name.</returns>
    public static string Hyphenate(string name)
    {
        StringBuilder result = new();

        foreach (char character in name)
        {
            if (char.IsUpper(character) && result.Length > 0)
            {
                _ = result.Append('-');
            }

            _ = result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }
}
