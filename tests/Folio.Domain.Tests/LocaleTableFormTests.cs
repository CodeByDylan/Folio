using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Domain.Tests;

public sealed class LocaleTableFormTests
{
    [Test]
    public async Task A_Table_Header_Key_Loads_As_Its_Dotted_Form()
    {
        Result<Snapshot> result = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project]\nslug = \"a\"\n",
                [".folio/locales/en.toml"] = "[project]\ntagline = \"From a table\"\n",
            })
            .Resolve();

        ResolvedProject project = result.Value.Localizations[result.Value.DefaultLocale].Projects[0];

        await Assert.That(project.Tagline!.Value).IsEqualTo("From a table");
    }

    [Test]
    public async Task Dotted_And_Table_Forms_Load_Side_By_Side()
    {
        Result<Snapshot> result = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = """
                    version = 1

                    [project]
                    slug = "a"

                    [[links]]
                    type = "demo"
                    url  = "https://demo.example.com"
                    """,
                [".folio/locales/en.toml"] = "link.demo = \"Dotted\"\n\n[project]\nname = \"Table\"\n",
            })
            .Resolve();

        ResolvedProject project = result.Value.Localizations[result.Value.DefaultLocale].Projects[0];

        await Assert.That(project.Name!.Value).IsEqualTo("Table");
        await Assert.That(project.Links[0].Label!.Value).IsEqualTo("Dotted");
    }

    [Test]
    public async Task A_Section_Only_In_A_Sibling_Locale_Is_Not_Reported_As_Missing_Everywhere()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid("\"en\", \"nl\"")
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                [".folio/content/nl/s.md"] = "# S\n",
            })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.SectionMissingChain);
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.SectionMissingAllLocales);
    }

    [Test]
    public async Task A_Section_Resolved_By_Truncation_Reports_Truncation()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid("\"en\", \"nl\", \"nl-BE\"")
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                [".folio/content/nl/s.md"] = "# S\n",
                [".folio/content/en/s.md"] = "# S\n",
            })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LocaleTruncated);
    }

    [Test]
    public async Task Central_Content_Counts_Against_An_Empty_Locale()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid("\"en\", \"nl\"")
            .Central(".folio/content/nl/about.md", "# Over\n")
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.LocaleEmpty);
    }
}
