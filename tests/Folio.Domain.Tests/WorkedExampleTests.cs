using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Resolution;
using Loom.Results;
using TUnit.Assertions.Enums;

namespace Folio.Domain.Tests;

public sealed class WorkedExampleTests
{
    private static readonly LocaleTag English = Parse("en");
    private static readonly LocaleTag Dutch = Parse("nl");

    [Test]
    public async Task The_Portfolio_Resolves_Into_Every_Declared_Locale()
    {
        Snapshot snapshot = Resolve();

        await Assert.That(snapshot.Localizations.Keys).IsEquivalentTo([English, Dutch]);
        await Assert.That(snapshot.DefaultLocale).IsEqualTo(English);
        await Assert.That(snapshot.Id).IsNotEmpty();
    }

    [Test]
    public async Task Projects_Keep_The_Order_Declared_In_Projects_Toml()
    {
        Snapshot snapshot = Resolve();

        await Assert.That(snapshot.Localizations[English].Projects.Select(p => p.Slug.Value))
            .IsEquivalentTo(["folio", "folio-core", "cli"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Featured_Comes_From_The_Central_List()
    {
        Snapshot snapshot = Resolve();
        IReadOnlyList<ResolvedProject> projects = snapshot.Localizations[English].Projects;

        await Assert.That(projects[0].IsFeatured).IsTrue();
        await Assert.That(projects[1].IsFeatured).IsFalse();
    }

    [Test]
    public async Task Dutch_Strings_Resolve_Without_Fallback()
    {
        ResolvedProject folio = Project(Resolve(), Dutch, "folio");

        await Assert.That(folio.Tagline!.Value)
            .IsEqualTo("Portfolio's samengesteld uit de repos die ze beschrijven");
        await Assert.That(folio.Tagline.IsFallback).IsFalse();
        await Assert.That(folio.Tagline.Locale).IsEqualTo(Dutch);
    }

    [Test]
    public async Task A_Section_Missing_In_Dutch_Falls_Back_And_Reports()
    {
        Snapshot snapshot = Resolve();
        ResolvedProject folio = Project(snapshot, Dutch, "folio");

        ResolvedSection overview = folio.Sections.Single(s => s.Id == "overview");
        ResolvedSection architecture = folio.Sections.Single(s => s.Id == "architecture");

        await Assert.That(overview.Title!.Value).IsEqualTo("Overzicht");
        await Assert.That(overview.Body!.IsFallback).IsFalse();

        await Assert.That(architecture.Title!.Value).IsEqualTo("Architecture");
        await Assert.That(architecture.Body!.IsFallback).IsTrue();
        await Assert.That(architecture.Body.Locale).IsEqualTo(English);

        await Assert.That(snapshot.Diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.SectionMissingLocale);
    }

    [Test]
    public async Task Titles_Are_Taken_From_The_H1_And_Removed_From_The_Body()
    {
        ResolvedSection overview = Project(Resolve(), English, "folio").Sections.First();

        await Assert.That(overview.Title!.Value).IsEqualTo("Overview");
        await Assert.That(overview.Body!.Value).DoesNotContain("# Overview");
        await Assert.That(overview.Body.Value).StartsWith("Folio reads two sources");
    }

    [Test]
    public async Task Images_Become_Pinned_Raw_Urls_And_Sibling_Links_Become_Anchors()
    {
        ResolvedSection overview = Project(Resolve(), English, "folio").Sections.First();

        await Assert.That(overview.Body!.Value)
            .Contains("https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero.png");
        await Assert.That(overview.Body.Value).Contains("[architecture](#architecture)");
    }

    [Test]
    public async Task Links_Back_To_The_Site_Become_Root_Relative()
    {
        ResolvedSection overview = Project(Resolve(), English, "folio").Sections.First();

        await Assert.That(overview.Body!.Value).Contains("[folio-core](/projects/folio-core)");
    }

    [Test]
    public async Task Tags_Resolve_Against_The_Central_Vocabulary()
    {
        ResolvedProject folio = Project(Resolve(), Dutch, "folio");

        await Assert.That(folio.Tags.Select(t => t.Id)).IsEquivalentTo(["rust", "cli"], CollectionOrdering.Matching);
        await Assert.That(folio.Tags[0].Kind).IsEqualTo(TagKind.Language);
        await Assert.That(folio.Tags[0].Label!.Value).IsEqualTo("Rust");
    }

    [Test]
    public async Task A_Declared_Relation_Generates_Its_Inverse_On_The_Target()
    {
        Snapshot snapshot = Resolve();

        ResolvedRelation declared = Project(snapshot, Dutch, "folio").Relations.Single();
        ResolvedRelation generated = Project(snapshot, Dutch, "folio-core").Relations.Single();

        await Assert.That(declared.Type).IsEqualTo(RelationType.Uses);
        await Assert.That(declared.Target.Value).IsEqualTo("folio-core");
        await Assert.That(declared.IsGenerated).IsFalse();
        await Assert.That(declared.Label!.Value).IsEqualTo("Gebruikt");

        await Assert.That(generated.Type).IsEqualTo(RelationType.UsedBy);
        await Assert.That(generated.Target.Value).IsEqualTo("folio");
        await Assert.That(generated.IsGenerated).IsTrue();
        await Assert.That(generated.Label!.Value).IsEqualTo("Gebruikt door");
    }

    [Test]
    public async Task Media_Carries_Its_Pinned_Url_Dimensions_And_Alt_Text()
    {
        Snapshot snapshot = Resolve(new Dictionary<string, MediaSize>(StringComparer.Ordinal)
        {
            [".folio/media/hero.png"] = new(1280, 720),
        });

        ResolvedMedia hero = Project(snapshot, Dutch, "folio").Media.Single(media => media.Role == "hero");

        await Assert.That(hero.Url.ToString())
            .IsEqualTo("https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero.png");
        await Assert.That(hero.Width).IsEqualTo(1280);
        await Assert.That(hero.Height).IsEqualTo(720);
        await Assert.That(hero.Alt!.Value).IsEqualTo("Het Folio-dashboard met drie projecten");
    }

    [Test]
    public async Task Unmeasured_Media_Warns_And_Omits_Its_Dimensions()
    {
        Snapshot snapshot = Resolve();
        ResolvedMedia hero = Project(snapshot, English, "folio").Media.Single(media => media.Role == "hero");

        await Assert.That(hero.Width).IsNull();
        await Assert.That(snapshot.Diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.MediaDimensionsUnreadable);
    }

    [Test]
    public async Task Site_Sections_Use_The_Same_Resolver()
    {
        Snapshot snapshot = Resolve();

        ResolvedSection about = snapshot.Localizations[Dutch].Sections.Single();

        await Assert.That(about.Id).IsEqualTo("about");
        await Assert.That(about.Title!.Value).IsEqualTo("Over mij");
        await Assert.That(about.Body!.IsFallback).IsFalse();
    }

    [Test]
    public async Task Central_Section_Images_Are_Pinned_To_The_Central_Repository()
    {
        ResolvedSection about = Resolve().Localizations[English].Sections.Single();

        await Assert.That(about.Body!.Value)
            .Contains("https://raw.githubusercontent.com/dutchy/portfolio/centralsha/.folio/media/me.png");
    }

    [Test]
    public async Task A_Project_With_No_Sections_Reports_It_Once()
    {
        Snapshot snapshot = Resolve();

        await Assert.That(snapshot.Localizations[English].Projects.Single(p => p.Slug.Value == "folio-core").Sections)
            .IsEmpty();
        await Assert.That(snapshot.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.ProjectNoSections);
    }

    [Test]
    public async Task A_Monorepo_Package_Resolves_From_Its_Own_Subdirectory()
    {
        ResolvedProject cli = Project(Resolve(), English, "cli");

        await Assert.That(cli.Name!.Value).IsEqualTo("Tooling CLI");
        await Assert.That(cli.Sections.Single().Id).IsEqualTo("usage");
        await Assert.That(cli.Status).IsEqualTo(ProjectStatus.Maintenance);
    }

    [Test]
    public async Task A_Dark_Variant_Is_Carried_Beside_The_Hero()
    {
        IReadOnlyList<ResolvedMedia> media = Project(Resolve(), Dutch, "folio").Media;
        ResolvedMedia dark = media.Single(entry => entry.Role == "hero_dark");

        await Assert.That(media.Select(entry => entry.Role)).Contains("hero_dark");
        await Assert.That(dark.Url.ToString())
            .IsEqualTo("https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero-dark.png");
        await Assert.That(dark.Alt!.Value).IsEqualTo("Het Folio-dashboard in donkere modus");
    }

    [Test]
    public async Task Github_Metadata_Fills_The_Gaps()
    {
        ResolvedProject folio = Project(Resolve(), English, "folio");

        await Assert.That(folio.Status).IsEqualTo(ProjectStatus.Active);
        await Assert.That(folio.Role).IsEqualTo(ProjectRole.Author);
        await Assert.That(folio.Started).IsEqualTo("2026-03");
        await Assert.That(folio.Metadata.Stars).IsEqualTo(12);
        await Assert.That(folio.Metadata.License).IsEqualTo("MIT");
    }

    [Test]
    public async Task The_Snapshot_Id_Is_Stable_Across_Identical_Builds()
    {
        await Assert.That(Resolve().Id).IsEqualTo(Resolve().Id);
    }

    [Test]
    public async Task The_Snapshot_Id_Changes_With_The_Application_Version()
    {
        await Assert.That(Resolve(version: "1.0.0").Id).IsNotEqualTo(Resolve(version: "1.0.1").Id);
    }

    internal static Snapshot Resolve(
        IReadOnlyDictionary<string, MediaSize>? sizes = null,
        string version = "1.0.0")
    {
        string root = Fixture.Path_("worked-example");

        Result<Snapshot> result = new PortfolioResolver().Resolve(
            new CentralInput("dutchy/portfolio", "centralsha", Fixture.Load(Path.Combine(root, "central"))),
            [
                Fixture.Repo("dutchy/folio", Path.Combine(root, "repos", "folio"), sizes),
                Fixture.Repo("dutchy/folio-core", Path.Combine(root, "repos", "folio-core")),
                Fixture.Repo("dutchy/tooling", Path.Combine(root, "repos", "tooling"), path: "packages/cli"),
            ],
            version,
            new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error.Message);
    }

    private static ResolvedProject Project(Snapshot snapshot, LocaleTag locale, string slug) =>
        snapshot.Localizations[locale].Projects.Single(project => project.Slug.Value == slug);

    private static LocaleTag Parse(string tag)
    {
        _ = LocaleTag.TryParse(tag, out LocaleTag locale);
        return locale;
    }
}
