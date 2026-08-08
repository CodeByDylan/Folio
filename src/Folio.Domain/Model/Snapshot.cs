using Folio.Domain.Diagnostics;

namespace Folio.Domain.Model;

/// <summary>One complete, immutable resolution of the portfolio, in every declared locale.</summary>
/// <param name="Id">A content hash over the resolved portfolio and the application version.</param>
/// <param name="BuiltAt">When this snapshot finished building. Not serialized into a response body.</param>
/// <param name="DefaultLocale">The locale content falls back to.</param>
/// <param name="Localizations">One fully resolved site per declared locale.</param>
/// <param name="Diagnostics">Everything the resolver found, ordered deterministically.</param>
public sealed record Snapshot(
    string Id,
    DateTimeOffset BuiltAt,
    LocaleTag DefaultLocale,
    IReadOnlyDictionary<LocaleTag, ResolvedSite> Localizations,
    IReadOnlyList<Diagnostic> Diagnostics);
