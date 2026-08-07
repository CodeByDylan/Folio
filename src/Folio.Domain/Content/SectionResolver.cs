using System.Text;
using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;

namespace Folio.Domain.Content;

/// <summary>Resolves declared sections to their markdown for one locale.</summary>
internal sealed class SectionResolver(MarkdownRewriter rewriter)
{
    /// <summary>Resolves every declared section, falling back per locale.</summary>
    /// <param name="sections">The declared sections, in order.</param>
    /// <param name="files">The file set holding the content.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="locales">The chain to search, most specific first.</param>
    /// <param name="requested">The locale being resolved.</param>
    /// <param name="context">Where the files come from, for link rewriting.</param>
    /// <param name="sink">A sink scoped to the owner.</param>
    /// <returns>The resolved sections, minus any missing in every locale.</returns>
    public IReadOnlyList<ResolvedSection> Resolve(
        IReadOnlyList<SectionEntry> sections,
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> locales,
        LocaleTag requested,
        Func<string, MarkdownContext> context,
        DiagnosticSink sink)
    {
        List<ResolvedSection> resolved = [];

        foreach (SectionEntry section in sections)
        {
            ResolvedSection? one = ResolveOne(
                section, resolved.Count, sections, files, folioRoot, locales, requested, context, sink);

            if (one is not null)
            {
                resolved.Add(one);
            }
        }

        return resolved;
    }

    /// <summary>Turns a README into the single section a project with no sections may show.</summary>
    /// <param name="files">The repository's file set.</param>
    /// <param name="projectPath">The project's path within the repository, empty at its root.</param>
    /// <param name="defaultLocale">The locale a README is always attributed to.</param>
    /// <param name="requested">The locale being resolved.</param>
    /// <param name="context">Where the file comes from, for link rewriting.</param>
    /// <param name="sink">A sink scoped to the project.</param>
    /// <returns>The section, or <see langword="null" /> if the repository has no README.</returns>
    public ResolvedSection? ResolveReadme(
        FileSet files,
        string projectPath,
        LocaleTag defaultLocale,
        LocaleTag requested,
        Func<string, MarkdownContext> context,
        DiagnosticSink sink)
    {
        string? path = files.Paths
            .Where(candidate => ProjectLocation.IsReadme(candidate, projectPath))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        if (path is null || !files.TryGet(path, out ReadOnlyMemory<byte> contents))
        {
            return null;
        }

        RewrittenMarkdown? rewritten = rewriter.Rewrite(
            Encoding.UTF8.GetString(contents.Span),
            context(path),
            sink.ForFile(path));

        if (rewritten is null)
        {
            return null;
        }

        bool fallback = !requested.Equals(defaultLocale);

        return new ResolvedSection(
            "readme",
            new Localized<string>(rewritten.Title ?? "Readme", defaultLocale, fallback),
            new Localized<string>(rewritten.Body, defaultLocale, fallback),
            SectionSource.Readme);
    }

    private ResolvedSection? ResolveOne(
        SectionEntry section,
        int index,
        IReadOnlyList<SectionEntry> siblings,
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> locales,
        LocaleTag requested,
        Func<string, MarkdownContext> context,
        DiagnosticSink sink)
    {
        bool atRequestedLocale = true;

        foreach (LocaleTag locale in locales)
        {
            string path = $"{folioRoot}/content/{locale.Value}/{section.File}";

            if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
            {
                atRequestedLocale = false;
                continue;
            }

            DiagnosticSink file = sink.ForFile(path);
            string source = Encoding.UTF8.GetString(contents.Span);

            if (string.IsNullOrWhiteSpace(source))
            {
                file.Warning(DiagnosticCodes.SectionEmpty, $"Section '{section.Id}' is empty.");
            }

            MarkdownContext ctx = context(path) with
            {
                // Two sections may name the same file; the first declared wins rather than throwing.
                SectionIdByPath = siblings
                    .GroupBy(sibling => $"{folioRoot}/content/{locale.Value}/{sibling.File}", StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal),
            };
            RewrittenMarkdown? rewritten = rewriter.Rewrite(source, ctx, file);

            if (rewritten is null)
            {
                // Unparseable in this locale; fall through the chain as if the file were absent.
                atRequestedLocale = false;
                continue;
            }

            if (!atRequestedLocale)
            {
                bool truncated = requested.Value.StartsWith($"{locale.Value}-", StringComparison.Ordinal);

                sink.Info(
                    truncated ? DiagnosticCodes.LocaleTruncated : DiagnosticCodes.SectionMissingLocale,
                    truncated
                        ? $"Section '{section.Id}' resolved from {locale} for {requested}."
                        : $"Section '{section.Id}' has no {requested} file; used {locale}.",
                    pointer: $"/sections/{index}/body");
            }

            return new ResolvedSection(
                section.Id,
                new Localized<string>(rewritten.Title ?? Humanize(section.Id), locale, !atRequestedLocale),
                new Localized<string>(rewritten.Body, locale, !atRequestedLocale),
                SectionSource.Folio);
        }

        string prefix = $"{folioRoot}/content/";
        bool anywhere = files.Under($"{folioRoot}/content").Any(path =>
        {
            string rest = path[prefix.Length..];
            int slash = rest.IndexOf('/', StringComparison.Ordinal);

            return slash >= 0 && string.Equals(rest[(slash + 1)..], section.File, StringComparison.Ordinal);
        });

        if (anywhere)
        {
            sink.Warning(
                DiagnosticCodes.SectionMissingChain,
                $"Section '{section.Id}' has no file in {requested}'s fallback chain; "
                + "it was dropped from that locale.");
        }
        else
        {
            sink.Warning(
                DiagnosticCodes.SectionMissingAllLocales,
                $"Section '{section.Id}' has no file in any locale and was dropped.");
        }

        return null;
    }

    private static string Humanize(string id)
    {
        string spaced = id.Replace('-', ' ').Replace('_', ' ');

        return spaced.Length == 0 ? id : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
