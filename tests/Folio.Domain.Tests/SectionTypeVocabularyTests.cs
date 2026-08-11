using System.Text;
using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Tests;

public sealed class SectionTypeVocabularyTests
{
    [Test]
    public async Task Every_Section_Type_Is_Parseable_By_Its_Lowercase_Name()
    {
        foreach (SectionType type in Enum.GetValues<SectionType>())
        {
            DiagnosticSink sink = new();

            await Assert.That(Read(type.ToString().ToLowerInvariant(), sink)).IsEqualTo(type);
            await Assert.That(sink.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task An_Unknown_Type_Is_Reported_Rather_Than_Guessed()
    {
        DiagnosticSink sink = new();

        await Assert.That(Read("skills", sink)).IsNull();
        await Assert.That(sink.Diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.SchemaUnknownValue);
    }

    private static SectionType? Read(string value, DiagnosticSink sink)
    {
        _ = TomlDocumentReader.TryParse(
            Encoding.UTF8.GetBytes($"[section]\ntype = \"{value}\"\n"),
            DiagnosticCodes.CentralUnparseable,
            sink,
            out TomlDocumentReader document);

        return EnumNames.Section(document.Table("section")!, "type", sink);
    }
}
