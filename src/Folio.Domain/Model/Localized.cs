namespace Folio.Domain.Model;

/// <summary>A resolved value together with the locale it was found in.</summary>
/// <typeparam name="T">The type of the resolved value.</typeparam>
/// <param name="Value">The resolved value.</param>
/// <param name="Locale">The locale the value was found in.</param>
/// <param name="IsFallback">Whether <paramref name="Locale" /> differs from the one requested.</param>
public sealed record Localized<T>(T Value, LocaleTag Locale, bool IsFallback);
