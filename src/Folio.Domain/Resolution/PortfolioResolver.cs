using System.Security.Cryptography;
using System.Text;
using Folio.Domain.Configuration;
using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Domain.Resolution;

/// <summary>Resolves a complete portfolio from a set of files.</summary>
public sealed class PortfolioResolver
{
    /// <summary>The prefix marking a locale key as an interface string rather than content.</summary>
    private const string UiPrefix = "ui.";

    /// <summary>Resolves the portfolio in every declared locale.</summary>
    /// <param name="central">The central <c>.folio</c> contents.</param>
    /// <param name="repos">One entry per listed project, in display order.</param>
    /// <param name="applicationVersion">The resolver's own version, folded into the snapshot id.</param>
    /// <param name="builtAt">The build timestamp to stamp on the snapshot.</param>
    /// <param name="priorDiagnostics">Diagnostics from assembling the inputs, reported first.</param>
    /// <returns>The snapshot with its diagnostics, or a failure when the central config is fatally broken.</returns>
    public Result<Snapshot> Resolve(
        CentralInput central,
        IReadOnlyList<RepoInput> repos,
        string applicationVersion,
        DateTimeOffset builtAt,
        IReadOnlyList<Diagnostic>? priorDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(central);
        ArgumentNullException.ThrowIfNull(repos);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        DiagnosticSink sink = new();
        CentralConfigParser centralParser = new();

        if (!centralParser.TryParse(central.Files, sink, out CentralConfig config))
        {
            return FolioErrors.CentralConfigInvalid(sink.Diagnostics);
        }

        // The site cannot be dropped the way a project can, so the directory is simply never read.
        _ = ContentDirectoriesAreDeclared(
            central.Files, ".folio", config.Site.Locales, sink, "its content is ignored.");

        SitePath sitePath = new(config.Site.Url);
        MarkdownRewriter rewriter = new(sitePath);
        SectionResolver sectionResolver = new(rewriter);
        ProjectResolver projectResolver = new(sectionResolver);

        HashSet<string> alreadyReported =
        [
            .. (priorDiagnostics ?? [])
                .Where(diagnostic => diagnostic.Code == DiagnosticCodes.ProjectNotFound)
                .Select(diagnostic => diagnostic.Project ?? string.Empty),
        ];

        Dictionary<Slug, DiagnosticSink> projectSinks = [];
        List<ParsedProject> parsed = [];
        foreach (ProjectEntry entry in config.Projects)
        {
            RepoInput? repo = repos.FirstOrDefault(candidate =>
                string.Equals(candidate.Repo, entry.Repo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Path, entry.Path, StringComparison.Ordinal));

            if (repo is null)
            {
                string identity = ProjectLocation.Identity(entry.Repo, entry.Path);

                // Ingestion reports what it could not fetch; this covers only what it never saw.
                if (alreadyReported.Add(identity))
                {
                    sink.ForProject(identity).Error(
                        DiagnosticCodes.ProjectNotFound,
                        $"'{entry.Repo}' is listed but no content was supplied for it.");
                }

                continue;
            }

            ParsedProject? project = Parse(repo, entry, config, sink);

            if (project is not null)
            {
                parsed.Add(project);
            }
        }

        HashSet<Slug> slugs = [];
        Dictionary<Slug, string> taken = [];
        List<ParsedProject> unique = [];

        foreach (ParsedProject project in parsed)
        {
            if (slugs.Add(project.Slug))
            {
                unique.Add(project);
                projectSinks[project.Slug] = project.Sink;
                taken[project.Slug] = project.Input.Repo;
                continue;
            }

            // The first declaration keeps the identity; the collision costs one project, not the site.
            project.Sink.Error(
                DiagnosticCodes.CentralDuplicateSlug,
                $"'{project.Input.Repo}' resolves to slug '{project.Slug}', already taken by "
                + $"'{taken[project.Slug]}'; the later project was dropped.");

            foreach (Diagnostic diagnostic in project.Sink.Diagnostics)
            {
                Copy(diagnostic, sink);
            }
        }

        parsed = unique;

        if (parsed.Count == 0)
        {
            sink.Warning(DiagnosticCodes.PortfolioEmpty, "The portfolio contains no projects.");
        }

        RelationGraph graph = RelationGraph.Build(
            [.. parsed.Select(p => (p.Slug, p.Config.Relations))],
            slugs,
            slug => projectSinks[slug]);

        IReadOnlyDictionary<LocaleTag, LocaleBundle> centralBundles = LocaleBundle.ReadAll(
            central.Files, ".folio", config.Site.Locales, sink);

        LocaleResolver centralStrings = new(centralBundles, config.Site.DefaultLocale);

        Dictionary<LocaleTag, ResolvedSite> localizations = [];

        foreach (LocaleTag locale in config.Site.Locales)
        {
            localizations[locale] = ResolveSite(
                locale, central, config, centralStrings, rewriter, sectionResolver, projectResolver,
                parsed, graph, projectSinks, sink);
        }

        ReportLocaleCoverage(config, central.Files, parsed, centralBundles, sink);
        ReportLaggingVersions(parsed, sink);
        ReportOrphanedKeys(config, parsed, centralStrings, projectSinks, sink);

        IReadOnlyList<Diagnostic> diagnostics = Collate(
            config, parsed, projectSinks, sink.Diagnostics, priorDiagnostics ?? []);

        return new Snapshot(
            SnapshotId(central, repos, applicationVersion),
            builtAt,
            config.Site.DefaultLocale,
            localizations,
            diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Collate(
        CentralConfig config,
        IReadOnlyList<ParsedProject> parsed,
        IReadOnlyDictionary<Slug, DiagnosticSink> projectSinks,
        IReadOnlyList<Diagnostic> central,
        IReadOnlyList<Diagnostic> ingestion)
    {
        List<Diagnostic> all = [.. central, .. ingestion];

        foreach (ParsedProject project in parsed)
        {
            all.AddRange(projectSinks[project.Slug].Diagnostics);
        }
        List<Diagnostic> unique = [.. all.Distinct()];

        // Ordinal position of each project in projects.toml, by every name it can be reported under.
        Dictionary<string, int> order = new(StringComparer.Ordinal);
        Dictionary<string, int> byEntry = new(StringComparer.Ordinal);

        for (int index = 0; index < config.Projects.Count; index++)
        {
            ProjectEntry entry = config.Projects[index];
            string directory = ProjectLocation.Directory(entry.Repo, entry.Path);

            _ = order.TryAdd(entry.Repo, index);
            _ = order.TryAdd(directory, index);
            _ = byEntry.TryAdd(EntryKey(entry.Repo, entry.Path), index);

            if (Slug.TryDerive(directory, out Slug derived))
            {
                _ = order.TryAdd(derived.Value, index);
            }
        }

        foreach (ParsedProject project in parsed)
        {
            if (byEntry.TryGetValue(EntryKey(project.Input.Repo, project.Input.Path), out int index))
            {
                order[project.Slug.Value] = index;
            }
        }

        return
        [
            .. unique
                .Select((diagnostic, emitted) => (diagnostic, emitted))
                .OrderBy(entry => entry.diagnostic.Project is null ? -1 : Position(order, entry.diagnostic.Project))
                .ThenBy(entry => entry.emitted)
                .Select(entry => entry.diagnostic),
        ];
    }

    // A repository is matched case-insensitively, a path within it exactly.
    private static string EntryKey(string repo, string path) => $"{repo.ToLowerInvariant()}\0{path}";

    private static int Position(IReadOnlyDictionary<string, int> order, string project) =>
        order.TryGetValue(project, out int index) ? index : int.MaxValue;

    private static ResolvedSite ResolveSite(
        LocaleTag locale,
        CentralInput central,
        CentralConfig config,
        LocaleResolver centralStrings,
        MarkdownRewriter rewriter,
        SectionResolver sectionResolver,
        ProjectResolver projectResolver,
        IReadOnlyList<ParsedProject> parsed,
        RelationGraph graph,
        IReadOnlyDictionary<Slug, DiagnosticSink> projectSinks,
        DiagnosticSink sink)
    {
        IReadOnlyList<LocaleTag> chain = [.. centralStrings.Chain(locale)];

        IReadOnlyList<ResolvedProseSection> siteSections = sectionResolver.Resolve(
            config.Site.Sections,
            central.Files,
            ".folio",
            chain,
            locale,
            path => new MarkdownContext(path, central.Repo, central.PinnedSha,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            sink);

        List<ResolvedProject> projects = [.. parsed.Select(project => projectResolver.Resolve(
            project, locale, config.Site, centralStrings, config.Tags, graph, projectSinks[project.Slug]))];

        // A section missing in every locale is dropped by the resolver, so a page may list one that is gone.
        Dictionary<string, ResolvedSection> byId = siteSections.ToDictionary(
            section => section.Id, section => (ResolvedSection)section, StringComparer.Ordinal);

        foreach (SectionEntry entry in config.Site.Sections.Where(entry => entry.Hero is not null))
        {
            byId[entry.Id] = ResolveHero(entry, central, config, centralStrings, locale, sink);
        }

        foreach (SectionEntry entry in config.Site.Sections.Where(entry => entry.Skills is not null))
        {
            byId[entry.Id] = ResolveSkills(entry, centralStrings, locale, sink);
        }

        foreach (SectionEntry entry in config.Site.Sections.Where(entry => entry.Projects is not null))
        {
            byId[entry.Id] = new ResolvedProjectsSection(
                entry.Id,
                centralStrings.Resolve($"section.{entry.Id}.heading", locale, sink, "/heading"),
                entry.Projects!.Featured,
                entry.Projects.Limit);
        }

        foreach (SectionEntry entry in config.Site.Sections.Where(entry => entry.Type is SectionType.Contact))
        {
            string prefix = $"section.{entry.Id}";

            byId[entry.Id] = new ResolvedContactSection(
                entry.Id,
                centralStrings.Resolve($"{prefix}.heading", locale, sink, "/heading"),
                centralStrings.Resolve($"{prefix}.blurb", locale, sink, "/blurb"));
        }

        foreach (SectionEntry entry in config.Site.Sections.Where(entry => entry.Questions is not null))
        {
            byId[entry.Id] = ResolveQa(entry, central, rewriter, centralStrings, chain, locale, sink);
        }

        return new ResolvedSite(
            config.Site.Url,
            config.Site.DefaultLocale,
            config.Site.Locales,
            centralStrings.Resolve("site.title", locale, sink, "/title"),
            centralStrings.Resolve("site.tagline", locale, sink, "/tagline"),
            [.. config.Site.Links.Select((link, index) => new ResolvedSiteLink(
                link.Type,
                link.Url,
                centralStrings.Resolve(EnumNames.LinkKey(link.Type), locale, sink, $"/links/{index}/label")))],
            [.. config.Site.Pages.Select((page, index) => new ResolvedPage(
                page.Slug,
                page.IsHome,
                page.InNav,
                centralStrings.Resolve(EnumNames.PageKey(page.Slug), locale, sink, $"/pages/{index}/navLabel"),
                [.. page.Sections
                    .Select(id => byId.TryGetValue(id, out ResolvedSection? section) ? section : null)
                    .OfType<ResolvedSection>()]))],
            projects,
            Strings(centralStrings, locale, sink));
    }

    private static ResolvedSection ResolveHero(
        SectionEntry entry,
        CentralInput central,
        CentralConfig config,
        LocaleResolver strings,
        LocaleTag locale,
        DiagnosticSink sink)
    {
        HeroConfig hero = entry.Hero!;
        SitePath site = new(config.Site.Url);
        string prefix = $"section.{entry.Id}";

        List<ResolvedHeroAction> actions = [];

        foreach (HeroActionEntry action in hero.Actions)
        {
            // A link to this site is served as a path, the same rule markdown links follow.
            string url = site.TryMatch(action.Url, out string path) ? path : action.Url.ToString();

            actions.Add(new ResolvedHeroAction(
                action.Id,
                url,
                strings.Resolve(
                    $"{prefix}.action.{action.Id}",
                    locale,
                    sink,
                    $"/actions/{actions.Count}/label")));
        }

        List<ResolvedMedia> media = [];

        foreach ((string role, string reference) in hero.Media.OrderBy(item => item.Key, StringComparer.Ordinal))
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
                strings.Resolve($"{prefix}.media.{role}.alt", locale, sink, $"/media/{media.Count}/alt")));
        }

        return new ResolvedHeroSection(
            entry.Id,
            strings.Resolve($"{prefix}.headline", locale, sink, "/headline"),
            strings.Resolve($"{prefix}.subheadline", locale, sink, "/subheadline"),
            actions,
            media);
    }

    private static ResolvedSection ResolveSkills(
        SectionEntry entry,
        LocaleResolver strings,
        LocaleTag locale,
        DiagnosticSink sink)
    {
        string prefix = $"section.{entry.Id}";
        List<ResolvedSkillCategory> categories = [];

        foreach (SkillCategoryEntry category in entry.Skills!.Categories)
        {
            int index = categories.Count;

            categories.Add(new ResolvedSkillCategory(
                category.Id,
                strings.Resolve(
                    $"{prefix}.category.{category.Id}",
                    locale,
                    sink,
                    $"/categories/{index}/label"),
                [
                    .. category.Skills.Select((skill, position) => new ResolvedSkill(
                        skill.Id,
                        skill.Level,
                        strings.Resolve(
                            $"{prefix}.skill.{skill.Id}",
                            locale,
                            sink,
                            $"/categories/{index}/skills/{position}/label"))),
                ]));
        }

        return new ResolvedSkillsSection(entry.Id, categories);
    }

    private static ResolvedSection ResolveQa(
        SectionEntry entry,
        CentralInput central,
        MarkdownRewriter rewriter,
        LocaleResolver strings,
        IReadOnlyList<LocaleTag> chain,
        LocaleTag requested,
        DiagnosticSink sink)
    {
        string prefix = $"section.{entry.Id}";
        IReadOnlyList<string> declared = entry.Questions!;
        List<ResolvedQuestion> questions = [];

        // Answers follow the same fallback chain a prose body does.
        (IReadOnlyList<Answer> answers, LocaleTag found, string path) = Answers(entry, central, chain);
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

        bool fallback = !found.Equals(requested);

        foreach (string id in declared)
        {
            int index = questions.Count;

            if (!byId.TryGetValue(id, out Answer? answer))
            {
                file.Warning(
                    DiagnosticCodes.QaEntryMissing,
                    $"Entry '{id}' on '{entry.Id}' has no '## {id}' heading; it has no answer.");
            }

            MarkdownContext context = new(
                path, central.Repo, central.PinnedSha, new Dictionary<string, string>(StringComparer.Ordinal));

            RewrittenMarkdown? rewritten = answer is null
                ? null
                : rewriter.Rewrite(answer.Body, context, file);

            questions.Add(new ResolvedQuestion(
                id,
                strings.Resolve($"{prefix}.question.{id}", requested, sink, $"/questions/{index}/question"),
                rewritten is null ? null : new Localized<string>(rewritten.Body, found, fallback)));
        }

        return new ResolvedQaSection(entry.Id, questions);
    }

    /// <summary>Finds the first answers file in the chain and splits it.</summary>
    private static (IReadOnlyList<Answer> Answers, LocaleTag Locale, string Path) Answers(
        SectionEntry entry,
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

    /// <summary>Resolves every declared interface string for one locale.</summary>
    /// <param name="centralStrings">The central locale bundles.</param>
    /// <param name="locale">The locale being resolved.</param>
    /// <param name="sink">Where fallbacks are reported.</param>
    /// <returns>The strings, keyed as authored without the prefix.</returns>
    private static IReadOnlyDictionary<string, Localized<string>> Strings(
        LocaleResolver centralStrings,
        LocaleTag locale,
        DiagnosticSink sink)
    {
        Dictionary<string, Localized<string>> strings = new(StringComparer.Ordinal);

        foreach (string key in centralStrings.KeysUnder(UiPrefix))
        {
            string name = key[UiPrefix.Length..];

            if (centralStrings.Resolve(key, locale, sink, $"/strings/{name}") is { } value)
            {
                strings[name] = value;
            }
        }

        return strings;
    }

    private static ParsedProject? Parse(
        RepoInput repo,
        ProjectEntry entry,
        CentralConfig config,
        DiagnosticSink sink)
    {
        string folioRoot = ProjectLocation.FolioRoot(repo.Path);
        string directory = ProjectLocation.Directory(repo.Repo, repo.Path);
        string identity = ProjectLocation.Identity(repo.Repo, repo.Path);

        ProjectConfigParser parser = new();
        DiagnosticSink scratch = new();

        if (!parser.TryParse(repo.Files, folioRoot, scratch, out ProjectConfig projectConfig))
        {
            sink.ForProject(identity).Error(
                DiagnosticCodes.ProjectUnparseable,
                $"'{repo.Repo}' could not be parsed and was dropped.");

            foreach (Diagnostic diagnostic in scratch.Diagnostics)
            {
                Copy(diagnostic, sink.ForProject(identity));
            }

            return null;
        }
        bool resolved = projectConfig.Slug is { } authored
            ? Slug.TryParse(authored, out Slug slug)
            : Slug.TryDerive(directory, out slug);

        if (!resolved)
        {
            sink.ForProject(identity).Error(
                DiagnosticCodes.ProjectSlugInvalid,
                projectConfig.Slug is null
                    ? $"No slug could be derived from '{directory}'; set 'project.slug' explicitly."
                    : $"'{projectConfig.Slug}' is not a valid slug: lowercase letters, digits and "
                        + "hyphens only, not starting or ending with a hyphen.");
            return null;
        }

        DiagnosticSink projectSink = new DiagnosticSink().ForProject(slug.Value);

        foreach (Diagnostic diagnostic in scratch.Diagnostics)
        {
            Copy(diagnostic, projectSink);
        }

        if (!ContentDirectoriesAreDeclared(
                repo.Files, folioRoot, config.Site.Locales, projectSink, "the project was dropped."))
        {
            foreach (Diagnostic diagnostic in projectSink.Diagnostics)
            {
                Copy(diagnostic, sink);
            }

            return null;
        }

        IReadOnlyDictionary<LocaleTag, LocaleBundle> bundles = LocaleBundle.ReadAll(
            repo.Files, folioRoot, config.Site.Locales, projectSink);

        return new ParsedProject(slug, repo, folioRoot, entry, projectConfig, bundles, projectSink);
    }

    private static bool ContentDirectoriesAreDeclared(
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> declared,
        DiagnosticSink sink,
        string consequence)
    {
        string prefix = $"{folioRoot}/content/";
        HashSet<string> seen = new(StringComparer.Ordinal);
        bool valid = true;

        foreach (string path in files.Under($"{folioRoot}/content"))
        {
            string rest = path[prefix.Length..];
            int slash = rest.IndexOf('/', StringComparison.Ordinal);
            string directory = slash < 0 ? rest : rest[..slash];

            if (!seen.Add(directory))
            {
                continue;
            }

            if (slash < 0)
            {
                sink.Error(
                    DiagnosticCodes.LocaleContentDirUndeclared,
                    $"content/{rest} sits outside any locale directory; {consequence}");
                valid = false;
                continue;
            }

            if (!LocaleTag.TryParse(directory, out LocaleTag locale)
                || !declared.Contains(locale)
                || !string.Equals(locale.Value, directory, StringComparison.Ordinal))
            {
                sink.Error(
                    DiagnosticCodes.LocaleContentDirUndeclared,
                    $"content/{directory} names no declared locale; {consequence}");
                valid = false;
            }
        }

        return valid;
    }

    private static void ReportLocaleCoverage(
        CentralConfig config,
        FileSet centralFiles,
        IReadOnlyList<ParsedProject> parsed,
        IReadOnlyDictionary<LocaleTag, LocaleBundle> centralBundles,
        DiagnosticSink sink)
    {
        foreach (LocaleTag locale in config.Site.Locales)
        {
            if (locale.Equals(config.Site.DefaultLocale))
            {
                continue;
            }

            bool anywhere = centralBundles.ContainsKey(locale)
                || centralFiles.Paths.Any(path =>
                    path.StartsWith($".folio/content/{locale.Value}/", StringComparison.Ordinal))
                || parsed.Any(project => project.Locales.ContainsKey(locale))
                || parsed.Any(project => project.Input.Files.Paths.Any(path =>
                    path.Contains($"/content/{locale.Value}/", StringComparison.Ordinal)));

            if (!anywhere)
            {
                sink.Warning(DiagnosticCodes.LocaleEmpty, $"Locale {locale} has no content anywhere.");
            }
        }
    }

    private static void ReportLaggingVersions(IReadOnlyList<ParsedProject> parsed, DiagnosticSink sink)
    {
        string[] lagging =
        [
            .. parsed
                .Where(project => project.Config.Version < SchemaVersion.Current)
                .Select(project => $"{project.Slug.Value} (version {project.Config.Version})")
                .Order(StringComparer.Ordinal),
        ];

        if (lagging.Length > 0)
        {
            sink.Info(
                DiagnosticCodes.SchemaVersionLagging,
                $"Behind schema version {SchemaVersion.Current}: {string.Join(", ", lagging)}.");
        }
    }

    private static void ReportOrphanedKeys(
        CentralConfig config,
        IReadOnlyList<ParsedProject> parsed,
        LocaleResolver centralStrings,
        IReadOnlyDictionary<Slug, DiagnosticSink> projectSinks,
        DiagnosticSink sink)
    {
        HashSet<string> central = new(StringComparer.Ordinal) { "site.title", "site.tagline" };

        foreach (string key in centralStrings.KeysUnder(UiPrefix))
        {
            _ = central.Add(key);
        }

        foreach (SiteLinkEntry link in config.Site.Links)
        {
            _ = central.Add(EnumNames.LinkKey(link.Type));
        }

        foreach (PageEntry page in config.Site.Pages)
        {
            _ = central.Add(EnumNames.PageKey(page.Slug));
        }

        foreach (SectionEntry section in config.Site.Sections.Where(section => section.Hero is not null))
        {
            string prefix = $"section.{section.Id}";
            _ = central.Add($"{prefix}.headline");
            _ = central.Add($"{prefix}.subheadline");

            foreach (HeroActionEntry action in section.Hero!.Actions)
            {
                _ = central.Add($"{prefix}.action.{action.Id}");
            }

            foreach (string role in section.Hero.Media.Keys)
            {
                _ = central.Add($"{prefix}.media.{role}.alt");
            }
        }

        foreach (SectionEntry section in config.Site.Sections.Where(section => section.Projects is not null))
        {
            _ = central.Add($"section.{section.Id}.heading");
        }

        foreach (SectionEntry section in config.Site.Sections.Where(section => section.Type is SectionType.Contact))
        {
            _ = central.Add($"section.{section.Id}.heading");
            _ = central.Add($"section.{section.Id}.blurb");
        }

        foreach (SectionEntry section in config.Site.Sections.Where(section => section.Questions is not null))
        {
            foreach (string id in section.Questions!)
            {
                _ = central.Add($"section.{section.Id}.question.{id}");
            }
        }

        foreach (SectionEntry section in config.Site.Sections.Where(section => section.Skills is not null))
        {
            foreach (SkillCategoryEntry category in section.Skills!.Categories)
            {
                _ = central.Add($"section.{section.Id}.category.{category.Id}");

                foreach (SkillEntry skill in category.Skills)
                {
                    _ = central.Add($"section.{section.Id}.skill.{skill.Id}");
                }
            }
        }

        foreach (string id in config.Tags.Keys)
        {
            _ = central.Add($"tag.{id}");
        }

        foreach (RelationType type in RelationVocabulary.All)
        {
            _ = central.Add($"relation.{RelationVocabulary.Name(type)}");
        }

        centralStrings.ReportOrphanedKeys(central, sink);

        foreach (ParsedProject project in parsed)
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "project.name", "project.tagline" };

            foreach (LinkEntry link in project.Config.Links)
            {
                _ = keys.Add(EnumNames.LinkKey(link.Type));
            }

            foreach (string role in project.Config.Media.Keys)
            {
                _ = keys.Add($"media.{role}.alt");
            }

            new LocaleResolver(project.Locales, config.Site.DefaultLocale)
                .ReportOrphanedKeys(keys, projectSinks[project.Slug]);
        }
    }

    private static void Copy(Diagnostic diagnostic, DiagnosticSink sink)
    {
        DiagnosticSink scoped = diagnostic.Project is null ? sink : sink.ForProject(diagnostic.Project);
        scoped = diagnostic.File is null ? scoped : scoped.ForFile(diagnostic.File);

        switch (diagnostic.Severity)
        {
            case DiagnosticSeverity.Error:
                scoped.Error(diagnostic.Code, diagnostic.Message, diagnostic.Position, diagnostic.Pointer);
                break;
            case DiagnosticSeverity.Warning:
                scoped.Warning(diagnostic.Code, diagnostic.Message, diagnostic.Position, diagnostic.Pointer);
                break;
            default:
                scoped.Info(diagnostic.Code, diagnostic.Message, diagnostic.Position, diagnostic.Pointer);
                break;
        }
    }

    private static string SnapshotId(CentralInput central, IReadOnlyList<RepoInput> repos, string applicationVersion)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        hash.AppendData(Encoding.UTF8.GetBytes(applicationVersion));
        hash.AppendData(Encoding.UTF8.GetBytes($"{central.Repo} {central.PinnedSha}"));

        foreach (string path in central.Files.Paths.Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));

            if (central.Files.TryGet(path, out ReadOnlyMemory<byte> contents))
            {
                hash.AppendData(contents.Span);
            }
        }

        foreach (RepoInput repo in repos)
        {
            // Metadata and media sizes move without the commit moving, so the SHA alone would go stale.
            RepoMetadata metadata = repo.Metadata;

            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{repo.Repo} {repo.Path} {repo.PinnedSha} {metadata.Description} {metadata.Homepage} "
                + $"{string.Join(',', metadata.Topics)} {metadata.PrimaryLanguage} {metadata.Stars} "
                + $"{metadata.Forks} {metadata.License} {metadata.IsArchived} {metadata.CreatedAt:O} "
                + $"{metadata.PushedAt:O} "
                + $"{string.Join(',', metadata.Languages.Select(language => $"{language.Name}={language.Bytes}"))} "
                + $"{string.Join(',', metadata.Releases.Select(release =>
                    $"{release.TagName}@{release.PublishedAt:O}={release.Name}|{release.Url}|{release.IsPrerelease}"))} "
                + $"{string.Join(',', repo.MediaSizes.OrderBy(size => size.Key, StringComparer.Ordinal)
                    .Select(size => $"{size.Key}={size.Value.Width}x{size.Value.Height}"))} "
                + $"{string.Join(',', repo.MediaPaths.Order(StringComparer.Ordinal))}"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }
}
