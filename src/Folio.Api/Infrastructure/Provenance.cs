using Folio.Domain.Model;

namespace Folio.Api.Infrastructure;

/// <summary>Where a value came from, when that is not the locale the caller asked for.</summary>
/// <param name="Locale">The locale the value was found in.</param>
/// <param name="Fallback">Always true; a value from the requested locale is simply absent from the map.</param>
internal sealed record ProvenanceEntry(string Locale, bool Fallback);

/// <summary>Flattens localized values, collecting a sparse pointer-keyed record of the fallbacks.</summary>
internal sealed class Provenance
{
    private readonly Dictionary<string, ProvenanceEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Gets the fallbacks recorded so far, keyed by RFC 6901 pointer.</summary>
    public IReadOnlyDictionary<string, ProvenanceEntry> Entries => _entries;

    /// <summary>Gets a view of this record whose pointers are relative to a prefix.</summary>
    /// <param name="root">The pointer prefix, empty when the value is at the document root.</param>
    /// <returns>The scoped view.</returns>
    public ProvenanceScope At(string root) => new(this, root);

    /// <summary>Unwraps a localized value, recording it only when it did not come from the requested locale.</summary>
    /// <param name="localized">The resolved value.</param>
    /// <param name="pointer">The pointer to the field this becomes.</param>
    /// <returns>The value, or <see langword="null" /> if nothing resolved.</returns>
    public string? Take(Localized<string>? localized, string pointer)
    {
        if (localized is null)
        {
            return null;
        }

        if (localized.IsFallback)
        {
            _entries[pointer] = new ProvenanceEntry(localized.Locale.Value, Fallback: true);
        }

        return localized.Value;
    }
}

/// <summary>A provenance record and the pointer prefix everything recorded through it sits under.</summary>
internal sealed class ProvenanceScope(Provenance provenance, string root)
{
    /// <summary>Unwraps a localized value, recording any fallback under this scope's prefix.</summary>
    /// <param name="localized">The resolved value.</param>
    /// <param name="pointer">The pointer to the field, relative to this scope.</param>
    /// <returns>The value, or <see langword="null" /> if nothing resolved.</returns>
    public string? Take(Localized<string>? localized, string pointer) =>
        provenance.Take(localized, root + pointer);
}
