using Folio.Domain.Content;
using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class MarkdownRewriterTests
{
    private static readonly MarkdownContext Context = new(
        ContainingFile: ".folio/content/en/overview.md",
        Repo: "dutchy/folio",
        PinnedSha: "abc123",
        SectionIdByPath: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".folio/content/en/architecture.md"] = "architecture",
        });

    [Test]
    public async Task A_Title_Containing_A_Rewritten_Link_Does_Not_Corrupt_The_Body()
    {
        (RewrittenMarkdown result, _) = Rewrite("# See [arch](./architecture.md) now\n\nBody.\n");

        await Assert.That(result.Title).IsEqualTo("See arch now");
        await Assert.That(result.Body).IsEqualTo("Body.");
    }

    [Test]
    public async Task The_Leading_H1_Becomes_The_Title_And_Leaves_The_Body()
    {
        (RewrittenMarkdown result, _) = Rewrite("# Overview\n\nSome prose.\n");

        await Assert.That(result.Title).IsEqualTo("Overview");
        await Assert.That(result.Body).IsEqualTo("Some prose.");
    }

    [Test]
    public async Task A_File_Without_A_Leading_H1_Has_No_Title()
    {
        (RewrittenMarkdown result, _) = Rewrite("Some prose.\n\n## Later\n");

        await Assert.That(result.Title).IsNull();
        await Assert.That(result.Body).StartsWith("Some prose.");
    }

    [Test]
    public async Task A_Second_H1_Warns_And_Is_Left_Alone()
    {
        (RewrittenMarkdown result, DiagnosticSink sink) = Rewrite("# Overview\n\n# Again\n\nProse.\n");

        await Assert.That(result.Body).Contains("# Again");
        await Assert.That(sink.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.SectionBodyH1);
    }

    [Test]
    public async Task Relative_Image_Paths_Become_Pinned_Raw_Urls()
    {
        (RewrittenMarkdown result, _) = Rewrite("# T\n\n![hero](../../media/hero.png)\n");

        await Assert.That(result.Body)
            .IsEqualTo("![hero](https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero.png)");
    }

    [Test]
    public async Task Root_Absolute_Image_Paths_Resolve_From_The_Repo_Root()
    {
        (RewrittenMarkdown result, _) = Rewrite("# T\n\n![hero](/.folio/media/hero.png)\n");

        await Assert.That(result.Body)
            .IsEqualTo("![hero](https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero.png)");
    }

    [Test]
    public async Task Sibling_Section_Links_Become_Anchors()
    {
        (RewrittenMarkdown result, _) = Rewrite("# T\n\nSee [arch](./architecture.md).\n");

        await Assert.That(result.Body).IsEqualTo("See [arch](#architecture).");
    }

    [Test]
    public async Task A_Sibling_Link_With_A_Fragment_Targets_The_Section_Once()
    {
        (RewrittenMarkdown result, DiagnosticSink sink) = Rewrite("# T\n\nSee [arch](./architecture.md#deep).\n");

        await Assert.That(result.Body).IsEqualTo("See [arch](#architecture).");
        await Assert.That(sink.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.MarkdownFragmentDropped);
    }

    [Test]
    public async Task A_Relative_Link_Matching_No_Section_Warns_And_Is_Left_Alone()
    {
        (RewrittenMarkdown result, DiagnosticSink sink) = Rewrite("# T\n\nSee [x](./missing.md).\n");

        await Assert.That(result.Body).Contains("./missing.md");
        await Assert.That(sink.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.MarkdownLinkUnresolved);
    }

    [Test]
    public async Task Internal_Absolute_Links_Lose_The_Origin_And_Path_Prefix()
    {
        (RewrittenMarkdown result, _) = Rewrite(
            "# T\n\nSee [core](https://dutchy.dev/portfolio/projects/folio-core).\n",
            "https://dutchy.dev/portfolio");

        await Assert.That(result.Body).IsEqualTo("See [core](/projects/folio-core).");
    }

    [Test]
    public async Task An_Internal_Link_Keeps_Its_Query_And_Fragment()
    {
        (RewrittenMarkdown result, _) = Rewrite(
            "# T\n\nSee [core](https://dutchy.dev/projects/folio-core?tab=readme#install).\n");

        await Assert.That(result.Body).IsEqualTo("See [core](/projects/folio-core?tab=readme#install).");
    }

    [Test]
    public async Task Same_Origin_Links_Outside_The_Prefix_Stay_External()
    {
        (RewrittenMarkdown result, _) = Rewrite(
            "# T\n\nSee [blog](https://dutchy.dev/blog/post).\n",
            "https://dutchy.dev/portfolio");

        await Assert.That(result.Body).IsEqualTo("See [blog](https://dutchy.dev/blog/post).");
    }

    [Test]
    public async Task A_Www_Near_Match_Stays_External_And_Reports()
    {
        (RewrittenMarkdown result, DiagnosticSink sink) =
            Rewrite("# T\n\nSee [x](https://www.dutchy.dev/projects/a).\n");

        await Assert.That(result.Body).Contains("https://www.dutchy.dev/projects/a");
        await Assert.That(sink.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.MarkdownHostNearMatch);
    }

    [Test]
    public async Task Raw_Html_Is_Stripped_With_A_Warning()
    {
        (RewrittenMarkdown result, DiagnosticSink sink) =
            Rewrite("# T\n\nText <b>bold</b> here.\n\n<p>block</p>\n");

        await Assert.That(result.Body).DoesNotContain("<b>");
        await Assert.That(result.Body).DoesNotContain("<p>");
        await Assert.That(result.Body).Contains("bold");
        await Assert.That(sink.Diagnostics.Select(d => d.Code)).Contains(DiagnosticCodes.MarkdownHtmlStripped);
    }

    [Test]
    public async Task Tables_Fences_And_Footnotes_Survive_Untouched()
    {
        const string body = """
            | a | b |
            |---|---|
            | 1 | 2 |

            ```mermaid
            graph TD; A-->B;
            ```

            Text[^1]

            [^1]: A footnote.
            """;

        (RewrittenMarkdown result, _) = Rewrite($"# T\n\n{body}\n");

        await Assert.That(result.Body).IsEqualTo(body);
    }

    [Test]
    public async Task An_Internal_Autolink_Stays_A_Link()
    {
        (RewrittenMarkdown result, _) = Rewrite("# T\n\nVisit https://dutchy.dev/projects/a for more.\n");

        await Assert.That(result.Body).IsEqualTo("Visit [https://dutchy.dev/projects/a](/projects/a) for more.");
    }

    [Test]
    public async Task An_External_Autolink_Is_Left_Alone()
    {
        (RewrittenMarkdown result, _) = Rewrite("# T\n\nVisit https://example.com/x for more.\n");

        await Assert.That(result.Body).IsEqualTo("Visit https://example.com/x for more.");
    }

    private static (RewrittenMarkdown Result, DiagnosticSink Sink) Rewrite(
        string source,
        string siteUrl = "https://dutchy.dev")
    {
        DiagnosticSink sink = new();
        MarkdownRewriter rewriter = new(new SitePath(new Uri(siteUrl)));

        return (rewriter.Rewrite(source, Context, sink), sink);
    }
}
