using System.Text.Json.Serialization;
using Folio.Api.Infrastructure;
using Folio.Domain.Model;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Pages.GetPage;

/// <summary>Reads one page and the sections it renders.</summary>
/// <param name="View">The snapshot and locale to read.</param>
/// <param name="Slug">The page to read.</param>
internal sealed record Request(SnapshotView View, string Slug);

/// <summary>One section of a page. The <c>type</c> discriminator says which shape it is.</summary>
/// <param name="Id">Stable within the site. The anchor and link target.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProseSectionView), "prose")]
[JsonDerivedType(typeof(HeroSectionView), "hero")]
[JsonDerivedType(typeof(SkillsSectionView), "skills")]
[JsonDerivedType(typeof(QaSectionView), "qa")]
[JsonDerivedType(typeof(ContactSectionView), "contact")]
[JsonDerivedType(typeof(ProjectsSectionView), "projects")]
internal abstract record PageSectionView(string Id);

/// <summary>A section of authored markdown.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Title">The section title.</param>
/// <param name="Body">The rewritten markdown body.</param>
/// <param name="Source">Where the body came from.</param>
internal sealed record ProseSectionView(
    string Id,
    string? Title,
    string? Body,
    [property: WireEnum(typeof(SectionSource))] string Source) : PageSectionView(Id);

/// <summary>The section a page opens with.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Headline">The claim the page opens with.</param>
/// <param name="Subheadline">The line beneath it.</param>
/// <param name="Actions">Calls to action, in declaration order.</param>
/// <param name="Media">Images by role, in role order.</param>
internal sealed record HeroSectionView(
    string Id,
    string? Headline,
    string? Subheadline,
    IReadOnlyList<HeroActionView> Actions,
    IReadOnlyList<HeroMediaView> Media) : PageSectionView(Id);

/// <summary>One call to action on a hero.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Url">An absolute URL, or a site-relative path when it points at this site.</param>
/// <param name="Label">The label to render.</param>
internal sealed record HeroActionView(string Id, string Url, string? Label);

/// <summary>Skills, grouped into categories.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Categories">The categories, in declaration order.</param>
internal sealed record SkillsSectionView(
    string Id,
    IReadOnlyList<SkillCategoryView> Categories) : PageSectionView(Id);

/// <summary>One category of skills.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Label">The label to render.</param>
/// <param name="Skills">The skills it holds, in declaration order.</param>
internal sealed record SkillCategoryView(string Id, string? Label, IReadOnlyList<SkillView> Skills);

/// <summary>One rated skill.</summary>
/// <param name="Id">Stable within the section.</param>
/// <param name="Level">How well it is known.</param>
/// <param name="Label">The label to render.</param>
internal sealed record SkillView(
    string Id,
    [property: WireEnum(typeof(SkillLevel))] string Level,
    string? Label);

/// <summary>Questions with their answers.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Questions">The entries, in declaration order.</param>
internal sealed record QaSectionView(
    string Id,
    IReadOnlyList<QuestionView> Questions) : PageSectionView(Id);

/// <summary>One question and its answer.</summary>
/// <param name="Id">Stable within the section. The anchor a deep link names.</param>
/// <param name="Question">The question to render.</param>
/// <param name="Answer">The rewritten markdown answer.</param>
internal sealed record QuestionView(string Id, string? Question, string? Answer);

/// <summary>An invitation to get in touch. The form itself belongs to the frontend.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Heading">The words that open the section.</param>
/// <param name="Blurb">The line beneath them.</param>
internal sealed record ContactSectionView(
    string Id,
    string? Heading,
    string? Blurb) : PageSectionView(Id);

/// <summary>A selection of the portfolio. The projects themselves come from <c>/v1/projects</c>.</summary>
/// <param name="Id">Stable within the site.</param>
/// <param name="Heading">The words that open the section.</param>
/// <param name="Featured">Whether to show only the projects the site highlights.</param>
/// <param name="Limit">How many to show at most.</param>
internal sealed record ProjectsSectionView(
    string Id,
    string? Heading,
    bool Featured,
    int? Limit) : PageSectionView(Id);

/// <summary>One image on a hero.</summary>
/// <param name="Role">The role it fills, such as <c>image</c>.</param>
/// <param name="Url">An absolute URL pinned to the central repository's commit.</param>
/// <param name="Width">The intrinsic width, if measured.</param>
/// <param name="Height">The intrinsic height, if measured.</param>
/// <param name="Alt">The localized alt text.</param>
internal sealed record HeroMediaView(string Role, string Url, int? Width, int? Height, string? Alt);

/// <summary>One page, including its section bodies.</summary>
/// <param name="RequestedLocale">The locale the caller asked for.</param>
/// <param name="Locale">The locale actually serving the response.</param>
/// <param name="Slug">The page's stable identity.</param>
/// <param name="Home">Whether this is the site's entry point, so a frontend can canonicalize its route.</param>
/// <param name="NavLabel">The label to render, in navigation and as the page heading.</param>
/// <param name="Sections">The sections it renders, in declaration order.</param>
/// <param name="Provenance">Fallbacks, keyed by RFC 6901 pointer.</param>
internal sealed record Response(
    string RequestedLocale,
    string Locale,
    string Slug,
    bool Home,
    string? NavLabel,
    IReadOnlyList<PageSectionView> Sections,
    IReadOnlyDictionary<string, ProvenanceEntry> Provenance);

internal sealed class Handler : IHandler<Request, Response>
{
    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Slug.TryParse(request.Slug, out Slug slug))
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.MalformedSlug(request.Slug));
        }

        ResolvedSite site = request.View.Snapshot.Localizations[request.View.Resolved];

        ResolvedPage? page = site.Pages.FirstOrDefault(candidate => candidate.Slug == slug);

        if (page is null)
        {
            return Task.FromResult<Result<Response>>(FolioApiErrors.UnknownPage(request.Slug));
        }

        Provenance provenance = new();
        ProvenanceScope scope = provenance.At(string.Empty);

        return Task.FromResult<Result<Response>>(new Response(
            request.View.Requested.Value,
            request.View.Resolved.Value,
            page.Slug.Value,
            page.IsHome,
            scope.Take(page.NavLabel, "/navLabel"),
            Sections(page, scope),
            provenance.Entries));
    }

    private static IReadOnlyList<PageSectionView> Sections(ResolvedPage page, ProvenanceScope scope) =>
        [.. page.Sections.Select((section, index) => Section(section, index, scope))];

    /// <remarks>Every case is a type, so a new section type will not compile until it is mapped.</remarks>
    private static PageSectionView Section(ResolvedSection section, int index, ProvenanceScope scope) =>
        section switch
        {
            ResolvedProseSection prose => new ProseSectionView(
                prose.Id,
                scope.Take(prose.Title, $"/sections/{index}/title"),
                scope.Take(prose.Body, $"/sections/{index}/body"),
                Wire.Lower(prose.Source)),
            ResolvedHeroSection hero => Hero(hero, index, scope),
            ResolvedSkillsSection skills => Skills(skills, index, scope),
            ResolvedQaSection qa => Qa(qa, index, scope),
            ResolvedContactSection contact => new ContactSectionView(
                contact.Id,
                scope.Take(contact.Heading, $"/sections/{index}/heading"),
                scope.Take(contact.Blurb, $"/sections/{index}/blurb")),
            ResolvedProjectsSection projects => new ProjectsSectionView(
                projects.Id,
                scope.Take(projects.Heading, $"/sections/{index}/heading"),
                projects.Featured,
                projects.Limit),
            _ => throw new NotSupportedException($"No wire shape for {section.GetType().Name}."),
        };

    private static HeroSectionView Hero(ResolvedHeroSection hero, int index, ProvenanceScope scope) =>
        new(
            hero.Id,
            scope.Take(hero.Headline, $"/sections/{index}/headline"),
            scope.Take(hero.Subheadline, $"/sections/{index}/subheadline"),
            [
                .. hero.Actions.Select((action, position) => new HeroActionView(
                    action.Id,
                    action.Url,
                    scope.Take(action.Label, $"/sections/{index}/actions/{position}/label"))),
            ],
            [
                .. hero.Media.Select((media, position) => new HeroMediaView(
                    media.Role,
                    media.Url.ToString(),
                    media.Width,
                    media.Height,
                    scope.Take(media.Alt, $"/sections/{index}/media/{position}/alt"))),
            ]);

    private static QaSectionView Qa(ResolvedQaSection qa, int index, ProvenanceScope scope) =>
        new(
            qa.Id,
            [
                .. qa.Questions.Select((question, position) => new QuestionView(
                    question.Id,
                    scope.Take(question.Question, $"/sections/{index}/questions/{position}/question"),
                    scope.Take(question.Answer, $"/sections/{index}/questions/{position}/answer"))),
            ]);

    private static SkillsSectionView Skills(ResolvedSkillsSection skills, int index, ProvenanceScope scope) =>
        new(
            skills.Id,
            [
                .. skills.Categories.Select((category, position) => new SkillCategoryView(
                    category.Id,
                    scope.Take(category.Label, $"/sections/{index}/categories/{position}/label"),
                    [
                        .. category.Skills.Select((skill, place) => new SkillView(
                            skill.Id,
                            Wire.Lower(skill.Level),
                            scope.Take(
                                skill.Label,
                                $"/sections/{index}/categories/{position}/skills/{place}/label"))),
                    ])),
            ]);
}
