using System.Text;
using Folio.Domain.Configuration;
using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;
using Folio.Domain.Resolution;

namespace Folio.Domain.Tests;

/// <summary>Resolves single sections without assembling a portfolio around them.</summary>
public sealed class SiteSectionResolverTests
{
    private static readonly LocaleTag English = Tag("en");

    [Test]
    public async Task Resolves_A_Contact_Section_On_Its_Own()
    {
        DiagnosticSink sink = new();

        ResolvedSection? resolved = Resolve(
            new ContactSectionEntry("reach"),
            """
            [section.reach]
            heading = "Say hello"
            blurb = "I read every message."
            """,
            sink);

        ResolvedContactSection contact = (ResolvedContactSection)resolved!;

        await Assert.That(contact.Heading!.Value).IsEqualTo("Say hello");
        await Assert.That(contact.Blurb!.Value).IsEqualTo("I read every message.");
        await Assert.That(sink.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Carries_A_Projects_Selection_Through_Untouched()
    {
        ResolvedProjectsSection projects = (ResolvedProjectsSection)Resolve(
            new ProjectsSectionEntry("work", true, 3),
            """
            [section.work]
            heading = "Selected work"
            """,
            new DiagnosticSink())!;

        await Assert.That(projects.Featured).IsTrue();
        await Assert.That(projects.Limit).IsEqualTo(3);
    }

    [Test]
    public async Task Resolves_Without_A_Blurb_When_None_Is_Authored()
    {
        DiagnosticSink sink = new();

        ResolvedContactSection contact = (ResolvedContactSection)Resolve(
            new ContactSectionEntry("reach"), "[section.reach]\nheading = \"Hi\"", sink)!;

        await Assert.That(contact.Heading!.Value).IsEqualTo("Hi");

        // A key absent from every locale is left null here; the orphan audit is what notices it.
        await Assert.That(contact.Blurb).IsNull();
        await Assert.That(sink.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Leaves_Prose_To_The_Resolver_That_Reads_Its_File()
    {
        await Assert.That(Resolve(new ProseSectionEntry("about", "about.md"), "[site]", new DiagnosticSink()))
            .IsNull();
    }

    private static LocaleTag Tag(string value)
    {
        _ = LocaleTag.TryParse(value, out LocaleTag tag);
        return tag;
    }

    private static ResolvedSection? Resolve(SectionEntry entry, string locale, DiagnosticSink sink)
    {
        FileSet files = new(
        [
            new KeyValuePair<string, ReadOnlyMemory<byte>>(
                ".folio/locales/en.toml", Encoding.UTF8.GetBytes(locale)),
        ]);

        CentralInput central = new(
            "owner/dotfolio",
            new string('a', 40),
            files,
            new Dictionary<string, MediaSize>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        Uri url = new("https://example.com/");

        LocaleResolver strings = new(
            LocaleBundle.ReadAll(files, ".folio", [English], new DiagnosticSink()), English);

        return new SiteSectionResolver(new MarkdownRewriter(new SitePath(url))).Resolve(
            entry,
            new SectionContext(central, new SitePath(url), strings, [English], English),
            sink);
    }
}
