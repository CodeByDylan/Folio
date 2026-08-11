namespace Folio.Domain.Model;

/// <summary>One unit of authored content, resolved for one locale.</summary>
/// <param name="Id">Stable within the project. The anchor, route fragment and link target.</param>
/// <param name="Type">What the section holds, and so what renders it.</param>
/// <param name="Title">The section title, taken from the file's first H1 or humanized from <paramref name="Id" />.</param>
/// <param name="Body">The rewritten markdown body, with the title H1 removed.</param>
/// <param name="Source">Whether the body was authored or lifted from a README.</param>
/// <param name="Hero">The hero's content, present only when <paramref name="Type" /> is a hero.</param>
public sealed record ResolvedSection(
    string Id,
    SectionType Type,
    Localized<string>? Title,
    Localized<string>? Body,
    SectionSource Source,
    ResolvedHero? Hero = null);

/// <summary>A hero's content, resolved for one locale.</summary>
/// <param name="Headline">The claim the page opens with.</param>
/// <param name="Subheadline">The line beneath it.</param>
/// <param name="Actions">Calls to action, in declaration order.</param>
/// <param name="Media">Images by role, in role order.</param>
public sealed record ResolvedHero(
    Localized<string>? Headline,
    Localized<string>? Subheadline,
    IReadOnlyList<ResolvedHeroAction> Actions,
    IReadOnlyList<ResolvedMedia> Media);

/// <summary>One call to action, with its localized label.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Url">An absolute URL, or a site-relative path when it points at this site.</param>
/// <param name="Label">The label to render.</param>
public sealed record ResolvedHeroAction(string Id, string Url, Localized<string>? Label);
