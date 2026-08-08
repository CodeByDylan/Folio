using System.Text;
using Folio.Domain.Diagnostics;
using Folio.Domain.Toml;
using TUnit.Assertions.Enums;

namespace Folio.Domain.Tests;

public sealed class TomlDocumentReaderTests
{
    private const string ProjectToml = """
        version = 1

        [project]
        slug   = "folio"
        status = "active"
        tags   = ["rust", "cli"]

        [project.media]
        hero = ".folio/media/hero.png"

        [[links]]
        type = "demo"
        url  = "https://folio.example.com"

        [[links]]
        type = "docs"
        url  = "https://docs.example.com"
        """;

    [Test]
    public async Task Root_Keys_Are_Read_Before_Any_Table()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse(ProjectToml);

        await Assert.That(document.Root.Integer("version", sink)).IsEqualTo(1L);
    }

    [Test]
    public async Task Tables_Are_Indexed_By_Dotted_Path()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse(ProjectToml);

        await Assert.That(document.Table("project")!.String("slug", sink)).IsEqualTo("folio");
        await Assert.That(document.Table("project.media")!.String("hero", sink))
            .IsEqualTo(".folio/media/hero.png");
        await Assert.That(document.Table("missing")).IsNull();
    }

    [Test]
    public async Task Table_Arrays_Keep_Declaration_Order()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse(ProjectToml);

        IReadOnlyList<TomlTableReader> links = document.TableArray("links");

        await Assert.That(links).Count().IsEqualTo(2);
        await Assert.That(links[0].String("type", sink)).IsEqualTo("demo");
        await Assert.That(links[1].String("type", sink)).IsEqualTo("docs");
    }

    [Test]
    public async Task String_Arrays_Are_Read_In_Order()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse(ProjectToml);

        await Assert.That(document.Table("project")!.StringArray("tags", sink)).IsEquivalentTo(["rust", "cli"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Positions_Are_One_Based()
    {
        (TomlDocumentReader document, _) = Parse(ProjectToml);

        SourcePosition version = document.Root.PositionOf("version");
        SourcePosition slug = document.Table("project")!.PositionOf("slug");

        await Assert.That(version.Line).IsEqualTo(1);
        await Assert.That(version.Column).IsEqualTo(1);
        await Assert.That(slug.Line).IsEqualTo(4);
        await Assert.That(slug.Column).IsEqualTo(1);
    }

    [Test]
    public async Task A_Syntax_Error_Is_Reported_With_A_Position()
    {
        DiagnosticSink sink = new();

        bool parsed = TomlDocumentReader.TryParse(
            Encoding.UTF8.GetBytes("version = 1\nslug = \n"),
            DiagnosticCodes.ProjectUnparseable,
            sink,
            out _);

        await Assert.That(parsed).IsFalse();
        await Assert.That(sink.Diagnostics).IsNotEmpty();
        await Assert.That(sink.Diagnostics[0].Code).IsEqualTo(DiagnosticCodes.ProjectUnparseable);
        await Assert.That(sink.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(sink.Diagnostics[0].Position!.Line).IsEqualTo(2);
    }

    [Test]
    public async Task A_Value_Of_The_Wrong_Kind_Warns_And_Is_Ignored()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("[project]\nslug = 12\n");

        string? slug = document.Table("project")!.String("slug", sink);

        await Assert.That(slug).IsNull();
        await Assert.That(sink.Diagnostics[0].Code).IsEqualTo(DiagnosticCodes.SchemaInvalidValue);
        await Assert.That(sink.Diagnostics[0].Message).Contains("project.slug");
    }

    [Test]
    public async Task A_Non_String_Array_Element_Is_Dropped_With_A_Warning()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("[project]\ntags = [\"rust\", 3]\n");

        IReadOnlyList<string> tags = document.Table("project")!.StringArray("tags", sink);

        await Assert.That(tags).IsEquivalentTo(["rust"]);
        await Assert.That(sink.Diagnostics[0].Code).IsEqualTo(DiagnosticCodes.SchemaInvalidValue);
    }

    [Test]
    public async Task Unknown_Keys_Warn_And_Are_Ignored()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("[project]\nslug = \"folio\"\nstauts = \"active\"\n");

        document.Table("project")!.ReportUnknownKeys(new HashSet<string>(StringComparer.Ordinal) { "slug" }, sink);

        await Assert.That(sink.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(sink.Diagnostics[0].Code).IsEqualTo(DiagnosticCodes.SchemaUnknownKey);
        await Assert.That(sink.Diagnostics[0].Message).Contains("project.stauts");
        await Assert.That(sink.Diagnostics[0].Position!.Line).IsEqualTo(3);
    }

    [Test]
    public async Task Unknown_Tables_And_Root_Keys_Warn_In_Source_Order()
    {
        // The array is declared before the table, so a per-kind pass would report them out of order.
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("""
            verison = 1

            [[sectons]]
            id = "overview"

            [projct]
            slug = "folio"
            """);

        document.ReportUnknownStructure(
            new HashSet<string>(StringComparer.Ordinal) { "version" },
            new HashSet<string>(StringComparer.Ordinal) { "project" },
            new HashSet<string>(StringComparer.Ordinal) { "sections" },
            sink);

        await Assert.That(sink.Diagnostics.Select(diagnostic => diagnostic.Position!.Line))
            .IsEquivalentTo([1, 3, 6], CollectionOrdering.Matching);
        await Assert.That(sink.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct())
            .IsEquivalentTo([DiagnosticCodes.SchemaUnknownKey]);
        await Assert.That(sink.Diagnostics[1].Message).Contains("[[sectons]]");
        await Assert.That(sink.Diagnostics[2].Message).Contains("[projct]");
    }

    [Test]
    public async Task Every_Entry_Of_An_Unknown_Table_Array_Is_Reported()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("[[sectons]]\nid = \"a\"\n\n[[sectons]]\nid = \"b\"\n");
        HashSet<string> nothing = new(StringComparer.Ordinal);

        document.ReportUnknownStructure(nothing, nothing, nothing, sink);

        await Assert.That(sink.Diagnostics).Count().IsEqualTo(2);
    }

    [Test]
    public async Task A_Document_Matching_Its_Schema_Reports_Nothing()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse(ProjectToml);

        document.ReportUnknownStructure(
            new HashSet<string>(StringComparer.Ordinal) { "version" },
            new HashSet<string>(StringComparer.Ordinal) { "project", "project.media" },
            new HashSet<string>(StringComparer.Ordinal) { "links" },
            sink);

        await Assert.That(sink.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Locale_Files_Read_As_Dotted_Root_Keys()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("""
            project.name    = "Folio"
            project.tagline = "Assembled from the repos they describe"
            tag.rust        = "Rust"
            """);

        await Assert.That(document.Root.String("project.name", sink)).IsEqualTo("Folio");
        await Assert.That(document.Root.String("project.tagline", sink))
            .IsEqualTo("Assembled from the repos they describe");
        await Assert.That(document.Root.String("tag.rust", sink)).IsEqualTo("Rust");
    }

    [Test]
    public async Task Quoted_Keys_Are_Read_Without_Their_Quotes()
    {
        (TomlDocumentReader document, DiagnosticSink sink) = Parse("""
            relation."used-by" = "Used by"
            """);

        await Assert.That(document.Root.String("relation.used-by", sink)).IsEqualTo("Used by");
    }

    private static (TomlDocumentReader Document, DiagnosticSink Sink) Parse(string toml)
    {
        DiagnosticSink sink = new();

        bool parsed = TomlDocumentReader.TryParse(
            Encoding.UTF8.GetBytes(toml),
            DiagnosticCodes.ProjectUnparseable,
            sink,
            out TomlDocumentReader document);

        return parsed
            ? (document, sink)
            : throw new InvalidOperationException(
                $"Fixture failed to parse: {string.Join("; ", sink.Diagnostics.Select(d => d.Message))}");
    }
}
