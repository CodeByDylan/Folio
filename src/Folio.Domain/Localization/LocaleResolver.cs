using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Localization;

/// <summary>Resolves a key through the RFC 4647 lookup chain, reporting where the value came from.</summary>
internal sealed class LocaleResolver(
    IReadOnlyDictionary<LocaleTag, LocaleBundle> bundles,
    LocaleTag defaultLocale)
{
    /// <summary>Gets the chain a locale falls back through, most specific first.</summary>
    /// <param name="requested">The locale being resolved.</param>
    /// <returns>The requested locale, its truncations, then the default locale.</returns>
    public IEnumerable<LocaleTag> Chain(LocaleTag requested)
    {
        HashSet<LocaleTag> seen = [requested];

        yield return requested;

        LocaleTag current = requested;

        while (current.TryTruncate(out LocaleTag parent))
        {
            if (seen.Add(parent))
            {
                yield return parent;
            }

            current = parent;
        }

        if (seen.Add(defaultLocale))
        {
            yield return defaultLocale;
        }
    }

    /// <summary>Resolves one key, reporting a truncation or a missing translation.</summary>
    /// <param name="key">The dotted key.</param>
    /// <param name="requested">The locale being resolved.</param>
    /// <param name="sink">A sink scoped to the owning project.</param>
    /// <param name="pointer">The response field this key becomes, for the diagnostic.</param>
    /// <returns>The value with its provenance, or <see langword="null" /> if no locale declares it.</returns>
    public Localized<string>? Resolve(string key, LocaleTag requested, DiagnosticSink sink, string? pointer = null)
    {
        bool atRequestedLocale = true;

        foreach (LocaleTag candidate in Chain(requested))
        {
            if (!bundles.TryGetValue(candidate, out LocaleBundle? bundle) || !bundle.TryGet(key, out string value))
            {
                atRequestedLocale = false;
                continue;
            }

            if (atRequestedLocale)
            {
                return new Localized<string>(value, candidate, IsFallback: false);
            }

            // The default locale can also be a truncation of what was asked for, and then it is one.
            bool truncated = requested.Value.StartsWith($"{candidate.Value}-", StringComparison.Ordinal);

            sink.Info(
                truncated ? DiagnosticCodes.LocaleTruncated : DiagnosticCodes.LocaleKeyMissing,
                truncated
                    ? $"'{key}' resolved from {candidate} for {requested}."
                    : $"'{key}' is not translated into {requested}; used {candidate}.",
                pointer: pointer);

            return new Localized<string>(value, candidate, IsFallback: true);
        }

        return null;
    }

    /// <summary>Reports every declared key the configuration never reads.</summary>
    /// <param name="referenced">The keys the configuration actually uses.</param>
    /// <param name="sink">A sink scoped to the owning project.</param>
    public void ReportOrphanedKeys(IReadOnlySet<string> referenced, DiagnosticSink sink)
    {
        foreach (LocaleBundle bundle in bundles.Values.OrderBy(b => b.Locale.Value, StringComparer.Ordinal))
        {
            foreach (string key in bundle.Keys.Order(StringComparer.Ordinal))
            {
                if (!referenced.Contains(key))
                {
                    sink.Warning(
                        DiagnosticCodes.LocaleKeyOrphaned,
                        $"'{key}' in {bundle.Locale} matches nothing in the configuration.");
                }
            }
        }
    }
}
