using Folio.Domain.Configuration;
using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;

namespace Folio.Domain.Resolution;

/// <summary>Resolves one parsed project for one locale.</summary>
internal sealed class ProjectResolver(SectionResolver sections)
{
    /// <summary>Resolves a project.</summary>
    /// <param name="project">The parsed project.</param>
    /// <param name="locale">The locale being resolved.</param>
    /// <param name="site">The central site configuration.</param>
    /// <param name="central">The central locale resolver, for tag and relation labels.</param>
    /// <param name="tags">The central tag vocabulary.</param>
    /// <param name="relations">The portfolio relation graph.</param>
    /// <param name="sink">A sink scoped to the project.</param>
    /// <returns>The project resolved for that locale.</returns>
    public ResolvedProject Resolve(
        ParsedProject project,
        LocaleTag locale,
        SiteConfig site,
        LocaleResolver central,
        IReadOnlyDictionary<string, TagDefinition> tags,
        RelationGraph relations,
        DiagnosticSink sink)
    {
        LocaleResolver strings = new(project.Locales, site.DefaultLocale);
        IReadOnlyList<LocaleTag> chain = [.. strings.Chain(locale)];

        MarkdownContext Context(string path) => new(
            path,
            project.Input.Repo,
            project.Input.PinnedSha,
            new Dictionary<string, string>(StringComparer.Ordinal));

        IReadOnlyList<ResolvedSection> resolved = sections.Resolve(
            project.Config.Sections,
            project.Input.Files,
            project.FolioRoot,
            chain,
            locale,
            Context,
            sink);

        if (project.Config.Sections.Count == 0)
        {
            if (project.Entry.UseReadme)
            {
                ResolvedSection? readme = sections.ResolveReadme(
                    project.Input.Files, project.Input.Path, site.DefaultLocale, locale, Context, sink);

                if (readme is not null)
                {
                    resolved = [readme];
                    sink.Info(DiagnosticCodes.ProjectReadmeUsed, "The README was used as this project's only section.");
                }
                else
                {
                    sink.Info(DiagnosticCodes.ProjectNoSections, "This project contributes no sections.");
                }
            }
            else
            {
                sink.Info(DiagnosticCodes.ProjectNoSections, "This project contributes no sections.");
            }
        }
        else if (project.Entry.UseReadme && project.Config.Sections.Count > 0)
        {
            sink.Warning(
                DiagnosticCodes.ProjectReadmeIgnored,
                "'use_readme' was ignored because this project declares sections.");
        }

        return new ResolvedProject(
            project.Slug,
            project.Input.Repo,
            project.Input.PinnedSha,
            project.Entry.IsFeatured,
            strings.Resolve("project.name", locale, sink, "/name"),
            strings.Resolve("project.tagline", locale, sink, "/tagline"),
            Status(project),
            project.Config.Role,
            project.Config.Started,
            project.Config.Ended,
            ResolveTags(project, locale, central, tags, sink),
            ResolveLinks(project, locale, strings, sink),
            ResolveRelations(project, locale, central, relations, sink),
            ResolveMedia(project, locale, strings, sink),
            resolved,
            project.Input.Metadata);
    }

    private static ProjectStatus? Status(ParsedProject project) =>
        project.Input.Metadata.IsArchived ? ProjectStatus.Archived : project.Config.Status;

    private static IReadOnlyList<ResolvedTag> ResolveTags(
        ParsedProject project,
        LocaleTag locale,
        LocaleResolver central,
        IReadOnlyDictionary<string, TagDefinition> vocabulary,
        DiagnosticSink sink)
    {
        List<ResolvedTag> resolved = [];

        foreach (string id in project.Config.Tags)
        {
            if (!vocabulary.TryGetValue(id, out TagDefinition? definition))
            {
                sink.Warning(DiagnosticCodes.TagsUnknown, $"Tag '{id}' is not in the vocabulary and was dropped.");
                continue;
            }

            resolved.Add(new ResolvedTag(
                definition.Id,
                definition.Kind,
                central.Resolve($"tag.{definition.Id}", locale, sink, $"/tags/{resolved.Count}/label")));
        }

        return resolved;
    }

    private static IReadOnlyList<ResolvedLink> ResolveLinks(
        ParsedProject project,
        LocaleTag locale,
        LocaleResolver strings,
        DiagnosticSink sink) =>
        [.. project.Config.Links.Select((link, index) => new ResolvedLink(
            link.Type,
            link.Url,
            strings.Resolve(EnumNames.LinkKey(link.Type), locale, sink, $"/links/{index}/label")))];

    private static IReadOnlyList<ResolvedRelation> ResolveRelations(
        ParsedProject project,
        LocaleTag locale,
        LocaleResolver central,
        RelationGraph relations,
        DiagnosticSink sink) =>
        [.. relations.For(project.Slug).Select((edge, index) => new ResolvedRelation(
            edge.Type,
            edge.Target,
            central.Resolve($"relation.{RelationVocabulary.Name(edge.Type)}", locale, sink, $"/relations/{index}/label"),
            edge.Generated))];

    private static IReadOnlyList<ResolvedMedia> ResolveMedia(
        ParsedProject project,
        LocaleTag locale,
        LocaleResolver strings,
        DiagnosticSink sink)
    {
        List<ResolvedMedia> media = [];

        foreach ((string role, string reference) in project.Config.Media.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (LinkTarget.IsAbsolute(reference, out Uri external))
            {
                sink.Info(
                    DiagnosticCodes.MediaDimensionsExternal,
                    $"Media '{role}' is hosted outside the repository, so it was not measured.");

                media.Add(new ResolvedMedia(role, external, null, null, Alt(role, media.Count)));
                continue;
            }
            string? path = MediaReferenceReader.Resolve(reference, project.Input.Path);

            if (path is null || !project.Input.MediaPaths.Contains(path))
            {
                sink.Warning(
                    DiagnosticCodes.MediaNotFound,
                    $"Media '{role}' ({reference}) was not found at the pinned commit.");
                continue;
            }

            project.Input.MediaSizes.TryGetValue(path, out MediaSize? size);

            if (size is null)
            {
                sink.Warning(
                    DiagnosticCodes.MediaDimensionsUnreadable,
                    $"Media '{role}' has no readable image header; dimensions were omitted.");
            }

            media.Add(new ResolvedMedia(
                role,
                RawContentUrl.For(project.Input.Repo, project.Input.PinnedSha, path),
                size?.Width,
                size?.Height,
                Alt(role, media.Count)));
        }

        return media;

        Localized<string>? Alt(string role, int index) =>
            strings.Resolve($"media.{role}.alt", locale, sink, $"/media/{index}/alt");
    }
}
