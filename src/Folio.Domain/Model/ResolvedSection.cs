namespace Folio.Domain.Model;

/// <summary>One unit of authored content, resolved for one locale. Each type is one case.</summary>
/// <param name="Id">Stable within its owner. The anchor, route fragment and link target.</param>
/// <param name="Type">What the section holds, and so what renders it.</param>
public abstract record ResolvedSection(string Id, SectionType Type);

/// <summary>Authored markdown, resolved for one locale.</summary>
/// <param name="Id">Stable within its owner.</param>
/// <param name="Title">The section title, taken from the file's first H1 or humanized from <paramref name="Id" />.</param>
/// <param name="Body">The rewritten markdown body, with the title H1 removed.</param>
/// <param name="Source">Whether the body was authored or lifted from a README.</param>
public sealed record ResolvedProseSection(
    string Id,
    Localized<string>? Title,
    Localized<string>? Body,
    SectionSource Source) : ResolvedSection(Id, SectionType.Prose);

/// <summary>The section a page opens with, resolved for one locale.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Headline">The claim the page opens with.</param>
/// <param name="Subheadline">The line beneath it.</param>
/// <param name="Actions">Calls to action, in declaration order.</param>
/// <param name="Media">Images by role, in role order.</param>
public sealed record ResolvedHeroSection(
    string Id,
    Localized<string>? Headline,
    Localized<string>? Subheadline,
    IReadOnlyList<ResolvedHeroAction> Actions,
    IReadOnlyList<ResolvedMedia> Media) : ResolvedSection(Id, SectionType.Hero);

/// <summary>Skills grouped into categories, resolved for one locale.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Categories">The categories, in declaration order.</param>
public sealed record ResolvedSkillsSection(
    string Id,
    IReadOnlyList<ResolvedSkillCategory> Categories) : ResolvedSection(Id, SectionType.Skills);

/// <summary>Questions with their answers, resolved for one locale.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Questions">The entries, in declaration order.</param>
public sealed record ResolvedQaSection(
    string Id,
    IReadOnlyList<ResolvedQuestion> Questions) : ResolvedSection(Id, SectionType.Qa);

/// <summary>An invitation to get in touch, resolved for one locale.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Heading">The words that open the section.</param>
/// <param name="Blurb">The line beneath them.</param>
public sealed record ResolvedContactSection(
    string Id,
    Localized<string>? Heading,
    Localized<string>? Blurb) : ResolvedSection(Id, SectionType.Contact);

/// <summary>A selection of the portfolio, resolved for one locale.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Heading">The words that open the section.</param>
/// <param name="Featured">Whether to show only the projects the site highlights.</param>
/// <param name="Limit">How many to show at most, or <see langword="null" /> for all of them.</param>
public sealed record ResolvedProjectsSection(
    string Id,
    Localized<string>? Heading,
    bool Featured,
    int? Limit) : ResolvedSection(Id, SectionType.Projects);

/// <summary>One question and its answer, resolved for one locale.</summary>
/// <param name="Id">Stable within the section. The anchor a deep link names.</param>
/// <param name="Question">The question to render.</param>
/// <param name="Answer">The rewritten markdown answer.</param>
public sealed record ResolvedQuestion(
    string Id,
    Localized<string>? Question,
    Localized<string>? Answer);

/// <summary>One category of skills, resolved for one locale.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Label">The label to render.</param>
/// <param name="Skills">The skills it holds, in declaration order.</param>
public sealed record ResolvedSkillCategory(
    string Id,
    Localized<string>? Label,
    IReadOnlyList<ResolvedSkill> Skills);

/// <summary>One rated skill, resolved for one locale.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Level">How well it is known.</param>
/// <param name="Label">The label to render.</param>
public sealed record ResolvedSkill(string Id, SkillLevel Level, Localized<string>? Label);

/// <summary>One call to action, with its localized label.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Url">An absolute URL, or a site-relative path when it points at this site.</param>
/// <param name="Label">The label to render.</param>
public sealed record ResolvedHeroAction(string Id, string Url, Localized<string>? Label);
