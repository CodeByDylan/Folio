using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Domain.Tests;

public sealed class ParserCrashTests
{
    [Test]
    public async Task Deeply_Nested_Toml_Drops_The_Project_Not_The_Build()
    {
        string toml = "version = 1\n\n[project]\nslug = \"a\"\ntags = " + new string('[', 80) + new string(']', 80) + "\n";

        Result<Snapshot> result = Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = toml })
            .Project("dutchy/b", new() { [".folio/project.toml"] = "version = 1\n\n[project]\nslug = \"b\"\n" })
            .Resolve();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Localizations[result.Value.DefaultLocale].Projects.Select(p => p.Slug.Value))
            .Contains("b");
        await Assert.That(result.Value.Diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.ProjectUnparseable);
    }

    [Test]
    public async Task Deeply_Nested_Markdown_Drops_The_Section_Not_The_Build()
    {
        string markdown = new string('>', 200) + " far too deep\n";

        Result<Snapshot> result = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "[[sections]]\nid = \"s\"\nfile = \"s.md\"\n",
                [".folio/content/en/s.md"] = markdown,
            })
            .Resolve();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.MarkdownUnparseable);
        await Assert.That(result.Value.Localizations[result.Value.DefaultLocale].Projects[0].Sections)
            .IsEmpty();
    }
}
