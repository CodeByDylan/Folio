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

    private static object Section(ResolvedSection section) => new
    {
        section.Id,
        Type = section.Type.ToString(),
        Title = Value(section.Title),
        Body = Value(section.Body),
        Source = section.Source.ToString(),
        Hero = section.Hero is null ? null : new
        {
            Headline = Value(section.Hero.Headline),
            Subheadline = Value(section.Hero.Subheadline),
            Actions = section.Hero.Actions.Select(action => new
            {
                action.Id,
                action.Url,
                Label = Value(action.Label),
            }),
            Media = section.Hero.Media.Select(media => new
            {
                media.Role,
                Url = media.Url.ToString(),
                media.Width,
                media.Height,
                Alt = Value(media.Alt),
            }),
        },
    };

    private static object? Value(Localized<string>? localized) =>
        localized is null ? null : new { localized.Value, Locale = localized.Locale.Value, localized.IsFallback };
}
