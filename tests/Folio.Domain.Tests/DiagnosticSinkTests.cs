using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class DiagnosticSinkTests
{
    [Test]
    public async Task Scoped_Sinks_Stamp_Context_Onto_Everything_Written_To_Them()
    {
        DiagnosticSink root = new();

        root.ForProject("folio")
            .ForFile(".folio/locales/nl.toml")
            .Warning(DiagnosticCodes.LocaleKeyMissing, "Missing key 'project.tagline'; fell back to 'en'");

        Diagnostic written = root.Diagnostics.Single();

        await Assert.That(written.Project).IsEqualTo("folio");
        await Assert.That(written.File).IsEqualTo(".folio/locales/nl.toml");
        await Assert.That(written.Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task Scoped_Sinks_Write_Through_To_The_Root_Buffer()
    {
        DiagnosticSink root = new();
        DiagnosticSink project = root.ForProject("folio");

        project.Info(DiagnosticCodes.SectionMissingLocale, "No Dutch architecture section.");
        root.Warning(DiagnosticCodes.PortfolioEmpty, "No projects.");

        await Assert.That(root.Diagnostics).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Diagnostics_Are_Kept_In_Emission_Order()
    {
        DiagnosticSink sink = new();

        sink.Info(DiagnosticCodes.LocaleTruncated, "nl-BE resolved to nl.");
        sink.Warning(DiagnosticCodes.TagsUnknown, "Unknown tag 'rst'.");

        await Assert.That(sink.Diagnostics[0].Code).IsEqualTo(DiagnosticCodes.LocaleTruncated);
        await Assert.That(sink.Diagnostics[1].Code).IsEqualTo(DiagnosticCodes.TagsUnknown);
    }

    [Test]
    public async Task An_Undeclared_Code_Is_A_Programming_Error()
    {
        DiagnosticSink sink = new();

        await Assert.That(() => sink.Warning("tags.mistyped", "…")).Throws<ArgumentException>();
    }
}
