using System.Text;
using Folio.Domain.Diagnostics;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Folio.Domain.Content;

/// <summary>What a section file resolved to.</summary>
/// <param name="Title">The text of the leading H1, if the file had one.</param>
/// <param name="Body">The markdown with the title removed and links rewritten.</param>
internal sealed record RewrittenMarkdown(string? Title, string Body);

/// <summary>Everything a rewrite needs to know about where a file came from.</summary>
/// <param name="ContainingFile">The repo-relative path of the markdown file.</param>
/// <param name="Repo">The repository, as <c>owner/name</c>.</param>
/// <param name="PinnedSha">The commit media URLs are pinned to.</param>
/// <param name="SectionIdByPath">Repo-relative paths of sibling sections, mapped to their ids.</param>
internal sealed record MarkdownContext(
    string ContainingFile,
    string Repo,
    string PinnedSha,
    IReadOnlyDictionary<string, string> SectionIdByPath);

/// <summary>Extracts a section's title and rewrites its links, leaving everything else untouched.</summary>
internal sealed class MarkdownRewriter(SitePath site)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>Rewrites one section file, or returns null when the markdown cannot be parsed.</summary>
    /// <param name="source">The file's markdown.</param>
    /// <param name="context">Where the file came from.</param>
    /// <param name="sink">A sink scoped to the file.</param>
    /// <returns>The title and the rewritten body, or null if the file is unparseable.</returns>
    public RewrittenMarkdown? Rewrite(string source, MarkdownContext context, DiagnosticSink sink)
    {
        MarkdownDocument document;

        try
        {
            document = Markdown.Parse(source, Pipeline);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Markdig throws past its nesting depth rather than reporting it; that section is unusable.
            sink.Warning(DiagnosticCodes.MarkdownUnparseable, $"The markdown could not be parsed: {exception.Message}");
            return null;
        }

        List<Edit> edits = [];

        string? title = TakeTitle(document, source, edits, sink);

        foreach (HtmlBlock block in document.Descendants<HtmlBlock>())
        {
            edits.Add(new Edit(block.Span.Start, block.Span.End + 1, string.Empty));
            sink.Warning(
                DiagnosticCodes.MarkdownHtmlStripped,
                "A raw HTML block was removed.",
                At(block.Line, block.Column));
        }

        foreach (MarkdownObject node in document.Descendants())
        {
            switch (node)
            {
                case HtmlInline inline:
                    edits.Add(new Edit(inline.Span.Start, inline.Span.End + 1, string.Empty));
                    sink.Warning(
                        DiagnosticCodes.MarkdownHtmlStripped,
                        $"Raw HTML '{inline.Tag}' was removed.",
                        At(inline.Line, inline.Column));
                    break;

                case LinkInline link when link.Url is { Length: > 0 }:
                    RewriteLink(link, context, edits, sink);
                    break;
            }
        }

        return new RewrittenMarkdown(title, Apply(source, edits));
    }

    private void RewriteLink(LinkInline link, MarkdownContext context, List<Edit> edits, DiagnosticSink sink)
    {
        string url = link.Url!;

        if (LinkTarget.IsAbsolute(url, out Uri absolute))
        {
            if (site.TryMatch(absolute, out string internalPath))
            {
                // An autolink's UrlSpan is its visible text, so replacing that alone would unlink it.
                edits.Add(link.IsAutoLink
                    ? new Edit(link.Span.Start, link.Span.End + 1, $"[{url}]({internalPath})")
                    : new Edit(link.UrlSpan.Start, link.UrlSpan.End + 1, internalPath));
            }
            else if (site.IsWwwNearMatch(absolute))
            {
                sink.Info(
                    DiagnosticCodes.MarkdownHostNearMatch,
                    $"'{url}' differs from the site host only by a 'www.' prefix and was left external.",
                    At(link.Line, link.Column));
            }

            return;
        }

        if (url.StartsWith('#'))
        {
            return;
        }

        string[] parts = url.Split('#', 2);
        string fragment = parts.Length > 1 ? "#" + parts[1] : string.Empty;
        string? resolved = RepoPath.Resolve(parts[0], context.ContainingFile);

        if (resolved is null)
        {
            sink.Warning(
                DiagnosticCodes.MarkdownLinkUnresolved,
                $"'{url}' escapes the repository; left as written.",
                At(link.Line, link.Column));
            return;
        }

        if (link.IsImage)
        {
            string raw = RawContentUrl.For(context.Repo, context.PinnedSha, resolved).OriginalString;
            edits.Add(new Edit(link.UrlSpan.Start, link.UrlSpan.End + 1, raw));
            return;
        }

        if (context.SectionIdByPath.TryGetValue(resolved, out string? sectionId))
        {
            // The section id is the fragment, so a link that already carried one has nowhere to put it.
            if (fragment.Length > 0)
            {
                sink.Warning(
                    DiagnosticCodes.MarkdownFragmentDropped,
                    $"'{url}' targets a fragment within another section; only the section was linked.",
                    At(link.Line, link.Column));
            }

            edits.Add(new Edit(link.UrlSpan.Start, link.UrlSpan.End + 1, $"#{sectionId}"));
            return;
        }

        if (resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            sink.Warning(
                DiagnosticCodes.MarkdownLinkUnresolved,
                $"'{url}' matches no declared section and was left as written.",
                At(link.Line, link.Column));
        }
    }

    private static string? TakeTitle(
        MarkdownDocument document,
        string source,
        List<Edit> edits,
        DiagnosticSink sink)
    {
        string? title = null;

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level != 1)
            {
                continue;
            }

            // Only the document's first block can match, so this branch is taken at most once.
            if (ReferenceEquals(document.FirstOrDefault(), heading))
            {
                title = Text(heading);
                edits.Add(new Edit(heading.Span.Start, SkipBlankLines(source, heading.Span.End + 1), string.Empty));
                continue;
            }

            if (title is not null)
            {
                sink.Warning(
                    DiagnosticCodes.SectionBodyH1,
                    "A second level-one heading appears in the body and was left as written.",
                    At(heading.Line, heading.Column));
            }
        }

        return title;
    }

    // Markdig positions are zero-based; the catalogue promises one-based.
    private static SourcePosition At(int line, int column) => new(line + 1, column + 1);

    private static string Text(HeadingBlock heading)
    {
        StringBuilder text = new();

        foreach (LiteralInline literal in heading.Inline?.Descendants<LiteralInline>() ?? [])
        {
            _ = text.Append(literal.Content.AsSpan());
        }

        return text.ToString().Trim();
    }

    private static int SkipBlankLines(string source, int index)
    {
        while (index < source.Length && (source[index] == '\n' || source[index] == '\r'))
        {
            index++;
        }

        return index;
    }

    private static string Apply(string source, List<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return source.Trim();
        }

        // A nested edit would apply against offsets its container has already shifted.
        List<Edit> outermost = [];
        int covered = -1;

        foreach (Edit edit in edits.OrderBy(edit => edit.Start).ThenByDescending(edit => edit.End))
        {
            if (edit.Start < covered)
            {
                continue;
            }

            outermost.Add(edit);
            covered = Math.Max(covered, edit.End);
        }

        StringBuilder result = new(source);

        foreach (Edit edit in outermost.OrderByDescending(edit => edit.Start))
        {
            _ = result.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Replacement);
        }

        return result.ToString().Trim();
    }

    private readonly record struct Edit(int Start, int End, string Replacement);
}
