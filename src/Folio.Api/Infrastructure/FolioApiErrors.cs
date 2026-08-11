using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Api.Infrastructure;

/// <summary>The failures the HTTP surface can return.</summary>
internal static class FolioApiErrors
{
    /// <summary>Creates the failure served before the first build completes.</summary>
    /// <returns>An error mapping to 503.</returns>
    public static Error NoSnapshot() => Errors.Unavailable(
        "folio.no_snapshot",
        "The portfolio has not been built yet.");

    /// <summary>Creates the failure for a locale that is not a well-formed tag.</summary>
    /// <param name="locale">What the caller sent.</param>
    /// <returns>An error mapping to 400.</returns>
    public static Error MalformedLocale(string locale) => Errors.Invalid(
        "folio.locale_malformed",
        $"'{locale}' is not a well-formed BCP-47 locale tag.");

    /// <summary>Creates the failure for a locale the site does not publish.</summary>
    /// <param name="requested">The locale asked for.</param>
    /// <param name="available">The locales the site publishes.</param>
    /// <returns>An error mapping to 400.</returns>
    public static Error UnservableLocale(LocaleTag requested, IEnumerable<LocaleTag> available) => Errors.Invalid(
        "folio.locale_unservable",
        $"This site does not publish '{requested}'. Available: "
        + $"{string.Join(", ", available.Select(locale => locale.Value).Order(StringComparer.Ordinal))}.");

    /// <summary>Creates the failure for a slug outside the permitted character set.</summary>
    /// <param name="slug">What the caller sent.</param>
    /// <returns>An error mapping to 400.</returns>
    public static Error MalformedSlug(string slug) => Errors.Invalid(
        "folio.slug_malformed",
        $"'{slug}' is not a valid slug: lowercase letters, digits and hyphens only.");

    /// <summary>Creates the failure for a severity filter that names no severity.</summary>
    /// <param name="severity">What the caller sent.</param>
    /// <returns>An error mapping to 400.</returns>
    public static Error UnknownSeverity(string severity) => Errors.Invalid(
        "folio.severity_unknown",
        $"'{severity}' is not a severity. Use info, warning or error.");

    /// <summary>Creates the failure for an unknown project slug.</summary>
    /// <param name="slug">The slug asked for.</param>
    /// <returns>An error mapping to 404.</returns>
    public static Error UnknownProject(string slug) => Errors.NotFound(
        "folio.project_unknown",
        $"No project with slug '{slug}'.");

    /// <summary>Creates the failure for an unknown page slug.</summary>
    /// <param name="slug">The slug asked for.</param>
    /// <returns>An error mapping to 404.</returns>
    public static Error UnknownPage(string slug) => Errors.NotFound(
        "folio.page_unknown",
        $"No page with slug '{slug}'.");
}
