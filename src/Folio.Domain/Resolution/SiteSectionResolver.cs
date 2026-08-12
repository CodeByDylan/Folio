using System.Text;
using Folio.Domain.Configuration;
using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;

namespace Folio.Domain.Resolution;

/// <summary>Everything a section needs beside its own declaration to resolve into one locale.</summary>
/// <param name="Central">The central <c>.folio</c> contents.</param>
/// <param name="Site">The site's own origin, used to serve links to itself as paths.</param>
/// <param name="Strings">The central locale bundles.</param>
/// <param name="Chain">The requested locale and its fallbacks, in order.</param>
/// <param name="Locale">The locale being resolved.</param>
internal sealed record SectionContext(
    CentralInput Central,
    SitePath Site,
    LocaleResolver Strings,
    IReadOnlyList<LocaleTag> Chain,
    LocaleTag Locale);

/// <summary>Resolves one declared site section into its wire shape, for one locale.</summary>
/// <param name="rewriter">Rewrites the markdown a Q&amp;A answer is authored in.</param>
internal sealed class SiteSectionResolver(MarkdownRewriter rewriter)
{
    /// <summary>Resolves one section.</summary>
    /// <param name="entry">The declared section.</param>
    /// <param name="context">What the section resolves against.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The resolved section, or <see langword="null" /> for prose, which reads its own file.</returns>
    public ResolvedSection? Resolve(SectionEntry entry, SectionContext context, DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        return entry switch
        {
            ProseSectionEntry => null,
            HeroSectionEntry hero => Hero(hero, context, sink),
            SkillsSectionEntry skills => Skills(skills, context, sink),
            QaSectionEntry qa => Qa(qa, context, sink),
            ContactSectionEntry contact => Contact(contact, context, sink),
            ProjectsSectionEntry selection => Projects(selection, context, sink),
            _ => throw new NotSupportedException($"No resolution for {entry.GetType().Name}."),
        };
    }

    private static ResolvedSection Hero(HeroSectionEntry entry, SectionContext context, DiagnosticSink sink)
    {
        CentralInput central = context.Central;
        List<ResolvedHeroAction> actions = [];

        foreach (HeroActionEntry action in entry.Actions)
        {
            // A link to this site is served as a path, the same rule markdown links follow.
            string url = context.Site.TryMatch(action.Url, out string path) ? path : action.Url.ToString();

            actions.Add(new ResolvedHeroAction(
                action.Id,
                url,
                context.Strings.Resolve(
                    SectionKeys.Action(entry.Id, action.Id),
                    context.Locale,
                    sink,
                    $"/actions/{actions.Count}/label")));
        }

        List<ResolvedMedia> media = [];

        foreach ((string role, string reference) in entry.Media.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            string path = $".folio/{reference.TrimStart('/')}";

            if (!central.MediaPaths.Contains(path))
            {
                sink.Warning(
                    DiagnosticCodes.MediaNotFound,
                    $"Media '{role}' on '{entry.Id}' ({reference}) was not found at the pinned commit.");
                continue;
            }

            central.MediaSizes.TryGetValue(path, out MediaSize? size);

            if (size is null)
            {
                sink.Warning(
                    DiagnosticCodes.MediaDimensionsUnreadable,
                    $"Media '{role}' on '{entry.Id}' has no readable image header; dimensions were omitted.");
            }

            media.Add(new ResolvedMedia(
                role,
                RawContentUrl.For(central.Repo, central.PinnedSha, path),
                size?.Width,
                size?.Height,
                context.Strings.Resolve(
                    SectionKeys.MediaAlt(entry.Id, role),
                    context.Locale,
                    sink,
                    $"/media/{media.Count}/alt")));
        }

        return new ResolvedHeroSection(
            entry.Id,
            context.Strings.Resolve(SectionKeys.Headline(entry.Id), context.Locale, sink, "/headline"),
            context.Strings.Resolve(SectionKeys.Subheadline(entry.Id), context.Locale, sink, "/subheadline"),
            actions,
            media);
    }

    private static ResolvedSection Skills(SkillsSectionEntry entry, SectionContext context, DiagnosticSink sink)
    {
        List<ResolvedSkillCategory> categories = [];

        foreach (SkillCategoryEntry category in entry.Categories)
        {
            int index = categories.Count;

            categories.Add(new ResolvedSkillCategory(
                category.Id,
                context.Strings.Resolve(
                    SectionKeys.Category(entry.Id, category.Id),
                    context.Locale,
                    sink,
                    $"/categories/{index}/label"),
                [
                    .. category.Skills.Select((skill, position) => new ResolvedSkill(
                        skill.Id,
                        skill.Level,
                        context.Strings.Resolve(
                            SectionKeys.Skill(entry.Id, skill.Id),
                            context.Locale,
                            sink,
                            $"/categories/{index}/skills/{position}/label"))),
                ]));
        }

        return new ResolvedSkillsSection(entry.Id, categories);
    }

    private ResolvedSection Qa(QaSectionEntry entry, SectionContext context, DiagnosticSink sink)
    {
        CentralInput central = context.Central;
        IReadOnlyList<string> declared = entry.Entries;
        List<ResolvedQuestion> questions = [];

        // Answers follow the same fallback chain a prose body does.
        (IReadOnlyList<Answer> answers, LocaleTag found, string path) = Answers(entry, central, context.Chain);
        Dictionary<string, Answer> byId = new(StringComparer.Ordinal);
        DiagnosticSink file = sink.ForFile(path);

        foreach (Answer answer in answers)
        {
            if (!declared.Contains(answer.Id, StringComparer.Ordinal))
            {
                file.Warning(
                    DiagnosticCodes.QaEntryUnknown,
                    $"'{answer.Id}' answers no declared entry on '{entry.Id}'; it was ignored.");
                continue;
            }

            byId[answer.Id] = answer;
        }

        bool fallback = !found.Equals(context.Locale);

        foreach (string id in declared)
        {
            int index = questions.Count;

            if (!byId.TryGetValue(id, out Answer? answer))
            {
                file.Warning(
                    DiagnosticCodes.QaEntryMissing,
                    $"Entry '{id}' on '{entry.Id}' has no '## {id}' heading; it has no answer.");
            }

            MarkdownContext markdown = new(
                path, central.Repo, central.PinnedSha, new Dictionary<string, string>(StringComparer.Ordinal));

            RewrittenMarkdown? rewritten = answer is null
                ? null
                : rewriter.Rewrite(answer.Body, markdown, file);

            questions.Add(new ResolvedQuestion(
                id,
                context.Strings.Resolve(
                    SectionKeys.Question(entry.Id, id),
                    context.Locale,
                    sink,
                    $"/questions/{index}/question"),
                rewritten is null ? null : new Localized<string>(rewritten.Body, found, fallback)));
        }

        return new ResolvedQaSection(entry.Id, questions);
    }

    private static ResolvedSection Contact(ContactSectionEntry entry, SectionContext context, DiagnosticSink sink) =>
        new ResolvedContactSection(
            entry.Id,
            context.Strings.Resolve(SectionKeys.Heading(entry.Id), context.Locale, sink, "/heading"),
            context.Strings.Resolve(SectionKeys.Blurb(entry.Id), context.Locale, sink, "/blurb"));

    private static ResolvedSection Projects(
        ProjectsSectionEntry entry,
        SectionContext context,
        DiagnosticSink sink) =>
        new ResolvedProjectsSection(
            entry.Id,
            context.Strings.Resolve(SectionKeys.Heading(entry.Id), context.Locale, sink, "/heading"),
            entry.Featured,
            entry.Limit);

    /// <summary>Finds the first answers file in the chain and splits it.</summary>
    private static (IReadOnlyList<Answer> Answers, LocaleTag Locale, string Path) Answers(
        QaSectionEntry entry,
        CentralInput central,
        IReadOnlyList<LocaleTag> chain)
    {
        foreach (LocaleTag locale in chain)
        {
            string path = $".folio/content/{locale.Value}/{entry.Id}.md";

            if (central.Files.TryGet(path, out ReadOnlyMemory<byte> contents))
            {
                return (AnswerSplitter.Split(Encoding.UTF8.GetString(contents.Span), out _), locale, path);
            }
        }

        return ([], chain[^1], $".folio/content/{chain[0].Value}/{entry.Id}.md");
    }
}
