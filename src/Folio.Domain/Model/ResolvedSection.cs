namespace Folio.Domain.Model;

/// <summary>One unit of authored content, resolved for one locale.</summary>
/// <param name="Id">Stable within the project. The anchor, route fragment and link target.</param>
/// <param name="Type">What the section holds, and so what renders it.</param>
/// <param name="Title">The section title, taken from the file's first H1 or humanized from <paramref name="Id" />.</param>
/// <param name="Body">The rewritten markdown body, with the title H1 removed.</param>
/// <param name="Source">Whether the body was authored or lifted from a README.</param>
public sealed record ResolvedSection(
    string Id,
    SectionType Type,
    Localized<string>? Title,
    Localized<string>? Body,
    SectionSource Source);
