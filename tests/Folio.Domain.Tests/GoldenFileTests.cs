using System.Text.Json;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class GoldenFileTests
{
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Test]
    [Arguments("en")]
    [Arguments("nl")]
    public async Task The_Resolved_Shape_Matches_Its_Golden_File(string locale)
    {
        _ = LocaleTag.TryParse(locale, out LocaleTag tag);

        ResolvedSite site = WorkedExampleTests.Resolve().Localizations[tag];
        string actual = JsonSerializer.Serialize(Shape(site), Indented);

        string path = Path.Combine(Fixture.Path_("worked-example"), $"expected.{locale}.json");

        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(actual.ReplaceLineEndings("\n"))
            .IsEqualTo((await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"));
    }

    /// <summary>Projects the resolved site onto the facts a golden file should pin.</summary>
    /// <param name="site">The resolved site.</param>
    /// <returns>A comparable shape.</returns>
    private static object Shape(ResolvedSite site) => new
    {
        site.Url,
        DefaultLocale = site.DefaultLocale.Value,
        Locales = site.Locales.Select(locale => locale.Value),
        Links = site.Links.Select(link => new
        {
            Type = link.Type.ToString(),
            Url = link.Url.ToString(),
            Label = Value(link.Label),
        }),
        Pages = site.Pages.Select(page => new
        {
            Slug = page.Slug.Value,
            page.IsHome,
            page.InNav,
            NavLabel = Value(page.NavLabel),
            Sections = page.Sections.Select(Section),
        }),
        Projects = site.Projects.Select(project => new
        {
            Slug = project.Slug.Value,
            project.Repo,
            project.IsFeatured,
            Name = Value(project.Name),
            Tagline = Value(project.Tagline),
            Status = project.Status?.ToString(),
            Role = project.Role?.ToString(),
            project.Started,
            project.Ended,
            Tags = project.Tags.Select(tag => new { tag.Id, Kind = tag.Kind?.ToString(), Label = Value(tag.Label) }),
            Links = project.Links.Select(link => new
            {
                Type = link.Type.ToString(),
                Url = link.Url.ToString(),
                Label = Value(link.Label),
            }),
            Relations = project.Relations.Select(relation => new
            {
                Type = relation.Type.ToString(),
                Target = relation.Target.Value,
                Label = Value(relation.Label),
                relation.IsGenerated,
            }),
            Media = project.Media.Select(media => new
            {
                media.Role,
                Url = media.Url.ToString(),
                media.Width,
                media.Height,
                Alt = Value(media.Alt),
            }),
            Sections = project.Sections.Select(Section),
            Languages = project.Metadata.LanguageShares.Select(language => new
            {
                language.Name,
                language.Bytes,
                language.Percent,
            }),
        }),
    };

    private static object Section(ResolvedSection section) => section switch
    {
        ResolvedProseSection prose => new
        {
            prose.Id,
            Type = prose.Type.ToString(),
            Title = Value(prose.Title),
            Body = Value(prose.Body),
            Source = prose.Source.ToString(),
        },
        ResolvedHeroSection hero => new
        {
            hero.Id,
            Type = hero.Type.ToString(),
            Headline = Value(hero.Headline),
            Subheadline = Value(hero.Subheadline),
            Actions = hero.Actions.Select(action => new
            {
                action.Id,
                action.Url,
                Label = Value(action.Label),
            }),
            Media = hero.Media.Select(media => new
            {
                media.Role,
                Url = media.Url.ToString(),
                media.Width,
                media.Height,
                Alt = Value(media.Alt),
            }),
        },
        ResolvedSkillsSection skills => new
        {
            skills.Id,
            Type = skills.Type.ToString(),
            Categories = skills.Categories.Select(category => new
            {
                category.Id,
                Label = Value(category.Label),
                Skills = category.Skills.Select(skill => new
                {
                    skill.Id,
                    Level = skill.Level.ToString(),
                    Label = Value(skill.Label),
                }),
            }),
        },
        ResolvedQaSection qa => new
        {
            qa.Id,
            Type = qa.Type.ToString(),
            Questions = qa.Questions.Select(question => new
            {
                question.Id,
                Question = Value(question.Question),
                Answer = Value(question.Answer),
            }),
        },
        ResolvedContactSection contact => new
        {
            contact.Id,
            Type = contact.Type.ToString(),
            Heading = Value(contact.Heading),
            Blurb = Value(contact.Blurb),
        },
        ResolvedProjectsSection projects => new
        {
            projects.Id,
            Type = projects.Type.ToString(),
            Heading = Value(projects.Heading),
            projects.Featured,
            projects.Limit,
        },
        _ => throw new NotSupportedException(section.GetType().Name),
    };

    private static object? Value(Localized<string>? localized) =>
        localized is null ? null : new { localized.Value, Locale = localized.Locale.Value, localized.IsFallback };
}
