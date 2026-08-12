using System.Text;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Resolution;

namespace Folio.Domain.Tests;

/// <summary>Runs the input audits without assembling a portfolio around them.</summary>
public sealed class PortfolioAuditTests
{
    [Test]
    public async Task Accepts_A_Declared_Locale_Directory()
    {
        DiagnosticSink sink = new();

        bool valid = PortfolioAudit.ContentDirectoriesAreDeclared(
            Files(".folio/content/en/about.md"), ".folio", [Tag("en")], sink, "it is ignored.");

        await Assert.That(valid).IsTrue();
        await Assert.That(sink.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Rejects_A_Directory_Naming_No_Declared_Locale()
    {
        DiagnosticSink sink = new();

        bool valid = PortfolioAudit.ContentDirectoriesAreDeclared(
            Files(".folio/content/fr/about.md"), ".folio", [Tag("en")], sink, "it is ignored.");

        await Assert.That(valid).IsFalse();
        await Assert.That(sink.Diagnostics.Single().Code)
            .IsEqualTo(DiagnosticCodes.LocaleContentDirUndeclared);
    }

    [Test]
    public async Task Rejects_A_File_Sitting_Outside_Any_Locale_Directory()
    {
        DiagnosticSink sink = new();

        bool valid = PortfolioAudit.ContentDirectoriesAreDeclared(
            Files(".folio/content/about.md"), ".folio", [Tag("en")], sink, "it is ignored.");

        await Assert.That(valid).IsFalse();
        await Assert.That(sink.Diagnostics.Single().Message).Contains("sits outside any locale directory");
    }

    [Test]
    public async Task Says_Nothing_When_No_Project_Lags_The_Schema()
    {
        DiagnosticSink sink = new();

        PortfolioAudit.ReportLaggingVersions([], sink);

        await Assert.That(sink.Diagnostics).IsEmpty();
    }

    private static LocaleTag Tag(string value)
    {
        _ = LocaleTag.TryParse(value, out LocaleTag tag);
        return tag;
    }

    private static FileSet Files(params string[] paths) =>
        new(paths.Select(path => new KeyValuePair<string, ReadOnlyMemory<byte>>(
            path, Encoding.UTF8.GetBytes("x"))));
}
