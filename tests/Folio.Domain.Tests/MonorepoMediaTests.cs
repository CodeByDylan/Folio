using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class MonorepoMediaTests
{
    [Test]
    public async Task A_Reference_Resolves_Against_The_Project_Root()
    {
        await Assert.That(MediaReferenceReader.Resolve(".folio/media/hero.png", "packages/cli"))
            .IsEqualTo("packages/cli/.folio/media/hero.png");
    }

    [Test]
    public async Task A_Reference_At_The_Repository_Root_Is_Unchanged()
    {
        await Assert.That(MediaReferenceReader.Resolve(".folio/media/hero.png", string.Empty))
            .IsEqualTo(".folio/media/hero.png");
    }

    [Test]
    public async Task A_Root_Absolute_Reference_Resolves_From_The_Repository_Root()
    {
        await Assert.That(MediaReferenceReader.Resolve("/shared/logo.png", "packages/cli"))
            .IsEqualTo("shared/logo.png");
    }

    [Test]
    public async Task A_Reference_Escaping_The_Repository_Is_Refused()
    {
        await Assert.That(MediaReferenceReader.Resolve("../../../etc/passwd", "packages/cli")).IsNull();
    }

    [Test]
    public async Task Declared_Media_Is_Read_From_A_Nested_Folio_Root()
    {
        FileSet files = new(
        [
            new("packages/cli/.folio/project.toml",
                System.Text.Encoding.UTF8.GetBytes(
                    "version = 1\n\n[project.media]\nhero = \".folio/media/hero.png\"\n")),
        ]);

        await Assert.That(MediaReferenceReader.Read(files, "packages/cli/.folio"))
            .IsEquivalentTo(["packages/cli/.folio/media/hero.png"]);
    }

    [Test]
    public async Task Media_In_The_Tree_But_Never_Fetched_Is_Still_Served()
    {
        // Ingestion fetches only .folio/**, so a file elsewhere is known from the tree alone.
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project(
                "dutchy/a",
                new() { [".folio/project.toml"] = "version = 1\n\n[project.media]\nhero = \"docs/shot.png\"\n" },
                mediaPaths: new HashSet<string>(StringComparer.Ordinal) { "docs/shot.png" })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.MediaNotFound);
    }

    [Test]
    public async Task Media_Declared_But_Absent_Anywhere_Is_Still_Not_Found()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project.media]\nhero = \"docs/gone.png\"\n",
            })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.MediaNotFound);
    }
}
