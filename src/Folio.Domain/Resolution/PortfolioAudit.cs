using Folio.Domain.Configuration;
using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;

namespace Folio.Domain.Resolution;

/// <summary>
/// The checks that judge a portfolio's inputs as a whole rather than resolve them: locale coverage,
/// schema drift and keys nothing reads.
/// </summary>
internal static class PortfolioAudit
{
    /// <summary>The prefix marking a locale key as an interface string rather than content.</summary>
    private const string UiPrefix = "ui.";

    /// <summary>Reports every content directory that names no declared locale.</summary>
    /// <param name="files">The files to walk.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="declared">The locales the config declares.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <param name="consequence">What happens to the offending content, appended to the message.</param>
    /// <returns><see langword="true" /> when every directory is declared.</returns>
    public static bool ContentDirectoriesAreDeclared(
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> declared,
        DiagnosticSink sink,
        string consequence)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(declared);

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

    /// <summary>Reports every declared locale that carries no content at all.</summary>
    /// <param name="config">The central config.</param>
    /// <param name="centralFiles">The central <c>.folio</c> contents.</param>
    /// <param name="parsed">The projects that survived parsing.</param>
    /// <param name="centralBundles">The central locale bundles that were read.</param>
    /// <param name="sink">Where problems are reported.</param>
    public static void ReportLocaleCoverage(
        CentralConfig config,
        FileSet centralFiles,
        IReadOnlyList<ParsedProject> parsed,
        IReadOnlyDictionary<LocaleTag, LocaleBundle> centralBundles,
        DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(centralBundles);

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

    /// <summary>Reports the projects still authored against an older schema version.</summary>
    /// <param name="parsed">The projects that survived parsing.</param>
    /// <param name="sink">Where the notice is reported.</param>
    public static void ReportLaggingVersions(IReadOnlyList<ParsedProject> parsed, DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(parsed);

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

    /// <summary>Reports every authored locale key nothing in the config reads.</summary>
    /// <param name="config">The central config.</param>
    /// <param name="parsed">The projects that survived parsing.</param>
    /// <param name="centralStrings">The central locale bundles.</param>
    /// <param name="projectSinks">Each project's own sink, so its keys are reported against it.</param>
    /// <param name="sink">Where central orphans are reported.</param>
    public static void ReportOrphanedKeys(
        CentralConfig config,
        IReadOnlyList<ParsedProject> parsed,
        LocaleResolver centralStrings,
        IReadOnlyDictionary<Slug, DiagnosticSink> projectSinks,
        DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(centralStrings);
        ArgumentNullException.ThrowIfNull(projectSinks);

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

        foreach (string key in config.Site.Sections.SelectMany(SectionKeys.All))
        {
            _ = central.Add(key);
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
}
