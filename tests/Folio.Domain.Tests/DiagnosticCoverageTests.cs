using System.Collections.Frozen;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

/// <summary>
/// Every catalogue code is produced by a scenario here, or explicitly accounted for. The table is
/// readonly data rather than a collector, so nothing is shared between parallel tests.
/// </summary>
public sealed class DiagnosticCoverageTests
{
    /// <summary>Codes produced outside the resolver, and covered by the assembly that produces them.</summary>
    private static readonly string[] CoveredElsewhere =
    [
        // Folio.Ingestion.Tests: GitHubContentSourceTests.
        DiagnosticCodes.ProjectNotFound,
        DiagnosticCodes.ProjectTreeTruncated,

        // Folio.Api.Tests: RefreshDiagnosticsTests. A failed first refresh has no snapshot to carry them.
        DiagnosticCodes.RefreshAbandoned,
        DiagnosticCodes.RefreshRateLimitInsufficient,

        // Folio.Ingestion.Tests: ReplayContentSourceTests.
        DiagnosticCodes.OverlayRootInvalid,
    ];

    /// <summary>Codes the resolver implements but cannot emit while only one schema version exists.</summary>
    private static readonly string[] AwaitingSchemaV2 = [DiagnosticCodes.SchemaVersionLagging];

    private static readonly FrozenDictionary<string, Func<IReadOnlyList<Diagnostic>>> Scenarios = new Dictionary<string, Func<IReadOnlyList<Diagnostic>>>(StringComparer.Ordinal)
    {
        [DiagnosticCodes.CentralMissing] = () =>
            Portfolio.Valid().Central(".folio/site.toml", null).Diagnostics(),

        [DiagnosticCodes.CentralUnparseable] = () =>
            Portfolio.Valid().Central(".folio/site.toml", "[site\nurl = ").Diagnostics(),

        [DiagnosticCodes.CentralDefaultLocaleUndeclared] = () =>
            Portfolio.Valid(defaultLocale: "de").Diagnostics(),

        [DiagnosticCodes.CentralDuplicateSlug] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nslug = \"same\"\n" })
            .Project("dutchy/b", new() { [".folio/project.toml"] = "[project]\nslug = \"same\"\n" })
            .Diagnostics(),


        [DiagnosticCodes.ProjectUnparseable] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project\nslug = " })
            .Diagnostics(),

        [DiagnosticCodes.ProjectSlugInvalid] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nslug = \"My_Project\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.ProjectNoSections] = () => Portfolio.Valid()
            .Project("dutchy/a")
            .Diagnostics(),

        [DiagnosticCodes.ProjectReadmeUsed] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { ["README.md"] = "# A\n\nProse.\n" },
                entry: "use_readme = true")
            .Diagnostics(),

        [DiagnosticCodes.ProjectReadmeIgnored] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new()
                {
                    ["README.md"] = "# A\n",
                    [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                    [".folio/content/en/s.md"] = "# S\n\nProse.\n",
                },
                entry: "use_readme = true")
            .Diagnostics(),

        [DiagnosticCodes.SchemaVersionUnsupportedHigh] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "version = 2\n" })
            .Diagnostics(),

        [DiagnosticCodes.SchemaVersionUnsupportedLow] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "version = 0\n" })
            .Diagnostics(),

        [DiagnosticCodes.SchemaVersionMissing] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nslug = \"a\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.SchemaUnknownKey] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nstauts = \"active\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.SchemaUnknownValue] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nstatus = \"bogus\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.SchemaInvalidValue] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nstarted = \"March 2026\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleContentDirUndeclared] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/content/de/x.md"] = "# X\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleUnparseable] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/locales/en.toml"] = "project.name = " })
            .Diagnostics(),

        [DiagnosticCodes.LocaleFileUndeclared] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/locales/EN.toml"] = "project.name = \"A\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleKeyMissing] = () => Portfolio.Valid("\"en\", \"nl\"")
            .Project(
                "dutchy/a",
                new() { [".folio/locales/en.toml"] = "project.name = \"A\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleKeyOrphaned] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/locales/en.toml"] = "project.name = \"A\"\nlink.demo = \"Demo\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleTruncated] = () => Portfolio.Valid("\"en\", \"nl\", \"nl-BE\"")
            .Project(
                "dutchy/a",
                new() { [".folio/locales/nl.toml"] = "project.name = \"A\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.LocaleEmpty] = () => Portfolio.Valid("\"en\", \"nl\"")
            .Project("dutchy/a")
            .Diagnostics(),

        [DiagnosticCodes.SectionMissingAllLocales] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.SectionMissingChain] = () => Portfolio.Valid("\"en\", \"nl\"")
            .Project(
                "dutchy/a",
                new()
                {
                    [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                    [".folio/content/nl/s.md"] = "# S\n",
                })
            .Diagnostics(),

        [DiagnosticCodes.SectionMissingLocale] = () => Portfolio.Valid("\"en\", \"nl\"")
            .Project(
                "dutchy/a",
                new()
                {
                    [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                    [".folio/content/en/s.md"] = "# S\n\nProse.\n",
                    [".folio/locales/nl.toml"] = "project.name = \"A\"\n",
                })
            .Diagnostics(),

        [DiagnosticCodes.SectionEmpty] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new()
                {
                    [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                    [".folio/content/en/s.md"] = "   ",
                })
            .Diagnostics(),

        [DiagnosticCodes.SectionBodyH1] = () => Section("# One\n\n# Two\n\nProse.\n"),

        [DiagnosticCodes.MarkdownUnparseable] = () =>
            Section("# S\n\n" + new string('>', 200) + " deep\n"),

        [DiagnosticCodes.MarkdownHtmlStripped] = () => Section("# S\n\nText <b>bold</b>.\n"),

        [DiagnosticCodes.MarkdownLinkUnresolved] = () => Section("# S\n\n[x](./nope.md)\n"),

        [DiagnosticCodes.MarkdownFragmentDropped] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new()
                {
                    [".folio/project.toml"] =
                        "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n\n[[sections]]\nid = \"t\"\nfile = \"t.md\"\n",
                    [".folio/content/en/s.md"] = "# S\n\n[x](./t.md#deep)\n",
                    [".folio/content/en/t.md"] = "# T\n",
                })
            .Diagnostics(),

        [DiagnosticCodes.MarkdownHostNearMatch] = () => Section("# S\n\n[x](https://www.dutchy.dev/a)\n"),

        [DiagnosticCodes.TagsUnknown] = () => Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\ntags = [\"nope\"]\n" })
            .Diagnostics(),

        [DiagnosticCodes.RelationsTargetUnknown] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/project.toml"] = "[[relations]]\ntype = \"uses\"\ntarget = \"ghost\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.MediaNotFound] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/project.toml"] = "[project.media]\nhero = \".folio/media/gone.png\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.MediaDimensionsUnreadable] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new()
                {
                    [".folio/project.toml"] = "[project.media]\nhero = \".folio/media/hero.png\"\n",
                    [".folio/media/hero.png"] = "not an image",
                })
            .Diagnostics(),

        [DiagnosticCodes.MediaDimensionsExternal] = () => Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/project.toml"] = "[project.media]\nhero = \"https://cdn.example.com/h.png\"\n" })
            .Diagnostics(),

        [DiagnosticCodes.PortfolioEmpty] = () => Portfolio.Valid().Diagnostics(),

        [DiagnosticCodes.SectionUnreferenced] = () => Site("""
            [[site.sections]]
            id   = "about"
            file = "about.md"
            """),

        [DiagnosticCodes.SectionFileUnexpected] = () => Site("""
            [[site.sections]]
            id = "about"
            """),

        [DiagnosticCodes.SectionDataUnreadable] = () => Site("""
            [[site.sections]]
            id   = "hero"
            type = "hero"
            """),

        [DiagnosticCodes.QaEntryMissing] = () => Qa(null),

        [DiagnosticCodes.QaEntryUnknown] = () => Qa("## why\n\nBecause.\n\n## stray\n\nNothing declares this.\n"),

        [DiagnosticCodes.PageSlugInvalid] = () => Site("""
            [[site.pages]]
            slug = "Not A Slug"
            """),

        [DiagnosticCodes.PageDuplicateSlug] = () => Site("""
            [[site.pages]]
            slug = "home"
            home = true

            [[site.pages]]
            slug = "home"
            """),

        [DiagnosticCodes.PageUnknownSection] = () => Site("""
            [[site.pages]]
            slug     = "home"
            home     = true
            sections = ["nope"]
            """),

        [DiagnosticCodes.PageNoHome] = () => Site("""
            [[site.pages]]
            slug = "about"
            """),

        [DiagnosticCodes.PageDuplicateHome] = () => Site("""
            [[site.pages]]
            slug = "home"
            home = true

            [[site.pages]]
            slug = "about"
            home = true
            """),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Resolves a portfolio whose <c>site.toml</c> carries one extra block.</summary>
    private static IReadOnlyList<Diagnostic> Qa(string? answers) => Portfolio.Valid()
        .Central(".folio/site.toml", $"""
            version = 1

            [site]
            url            = "https://dutchy.dev"
            default_locale = "en"
            locales        = ["en"]
            owner          = "dutchy"

            [[site.sections]]
            id   = "faq"
            type = "qa"

            [[site.pages]]
            slug     = "home"
            home     = true
            sections = ["faq"]
            """)
        .Central(".folio/sections/faq.toml", "version = 1\n\n[[entries]]\nid = \"why\"\n")
        .Central(".folio/content/en/faq.md", answers)
        .Diagnostics();

    private static IReadOnlyList<Diagnostic> Site(string extra) => Portfolio.Valid()
        .Central(".folio/site.toml", $"""
            version = 1

            [site]
            url            = "https://dutchy.dev"
            default_locale = "en"
            locales        = ["en"]
            owner          = "dutchy"

            {extra}
            """)
        .Diagnostics();

    [Test]
    [MethodDataSource(nameof(Codes))]
    public async Task The_Scenario_Produces_Its_Code(string code)
    {
        IReadOnlyList<Diagnostic> diagnostics = Scenarios[code]();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).Contains(code);
    }

    [Test]
    public async Task Every_Catalogue_Code_Is_Covered_Or_Accounted_For()
    {
        string[] unaccounted =
        [
            .. DiagnosticCodes.All
                .Except(Scenarios.Keys, StringComparer.Ordinal)
                .Except(CoveredElsewhere, StringComparer.Ordinal)
                .Except(AwaitingSchemaV2, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        await Assert.That(unaccounted).IsEmpty();
    }

    [Test]
    public async Task No_Scenario_Names_A_Code_That_Left_The_Catalogue()
    {
        string[] stale = [.. Scenarios.Keys.Except(DiagnosticCodes.All, StringComparer.Ordinal)];

        await Assert.That(stale).IsEmpty();
    }

    public static IEnumerable<Func<string>> Codes() =>
        Scenarios.Keys.Order(StringComparer.Ordinal).Select<string, Func<string>>(code => () => code);

    private static IReadOnlyList<Diagnostic> Section(string markdown) => Portfolio.Valid()
        .Project(
            "dutchy/a",
            new()
            {
                [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                [".folio/content/en/s.md"] = markdown,
            })
        .Diagnostics();
}
