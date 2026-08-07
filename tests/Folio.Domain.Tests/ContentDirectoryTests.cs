using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Domain.Tests;

public sealed class ContentDirectoryTests
{
    [Test]
    public async Task An_Undeclared_Central_Content_Directory_Is_Reported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Central(".folio/content/de/about.md", "# Über\n")
            .Diagnostics();

        Diagnostic? found = diagnostics.FirstOrDefault(
            diagnostic => diagnostic.Code == DiagnosticCodes.LocaleContentDirUndeclared);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Message).Contains("content/de");
        await Assert.That(found.Project).IsNull();
    }

    [Test]
    public async Task An_Undeclared_Central_Content_Directory_Does_Not_Abandon_The_Build()
    {
        Result<Snapshot> result = Portfolio.Valid()
            .Central(".folio/content/de/about.md", "# Über\n")
            .Resolve();

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task A_File_Directly_Under_Content_Is_Reported_Rather_Than_Ignored()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/content/about.md"] = "# About\n" })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LocaleContentDirUndeclared);
    }

    [Test]
    [Arguments(".folio/locales/en.tml")]
    [Arguments(".folio/locales/nl/strings.toml")]
    [Arguments(".folio/locales/notes.md")]
    public async Task A_Locale_File_That_Is_Not_A_Declared_Locale_Toml_Is_Reported(string path)
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new() { [path] = "x = 1\n" })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LocaleFileUndeclared);
    }

    [Test]
    public async Task A_Declared_Central_Content_Directory_Is_Not_Reported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid("\"en\", \"de\"")
            .Central(".folio/content/de/about.md", "# Über\n")
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.LocaleContentDirUndeclared);
    }
}
