using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class UnknownStructureTests
{
    [Test]
    public async Task A_Mistyped_Section_Table_Warns_Rather_Than_Dropping_Content_Silently()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[[sectons]]\nid = \"overview\"\nfile = \"overview.md\"\n",
                [".folio/content/en/overview.md"] = "# Overview\n",
            })
            .Diagnostics();

        await Assert.That(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.SchemaUnknownKey)
            .Select(diagnostic => diagnostic.Message))
            .Contains(message => message.Contains("[[sectons]]", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_Mistyped_Version_Key_Is_Reported_At_The_Document_Root()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Central(".folio/tags.toml", "verison = 1\n")
            .Diagnostics();

        await Assert.That(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.SchemaUnknownKey)
            .Select(diagnostic => diagnostic.File))
            .Contains(".folio/tags.toml");
    }

    [Test]
    public async Task Locale_Files_Keep_Their_Open_Key_Set()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project]\nslug = \"a\"\n",
                [".folio/locales/en.toml"] = "project.name = \"A\"\nanything.at.all = \"kept\"\n",
            })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.SchemaUnknownKey);
    }
}
