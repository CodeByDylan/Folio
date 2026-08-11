using Folio.Domain.Content;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses the three central files.</summary>
internal sealed class CentralConfigParser
{
    private static readonly HashSet<string> SiteKeys = new(StringComparer.Ordinal)
        { "url", "default_locale", "locales", "owner" };

    private static readonly HashSet<string> ProjectEntryKeys = new(StringComparer.Ordinal)
        { "repo", "path", "ref", "featured", "use_readme" };

    private static readonly HashSet<string> TagKeys = new(StringComparer.Ordinal) { "id", "kind" };

    private static readonly HashSet<string> LinkKeys = new(StringComparer.Ordinal) { "type", "url" };

    private static readonly HashSet<string> SectionKeys = new(StringComparer.Ordinal) { "id", "type", "file" };

    private static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal)
        { "slug", "home", "nav", "sections" };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version" };

    private static readonly HashSet<string> SiteTables = new(StringComparer.Ordinal) { "site" };

    private static readonly HashSet<string> SiteArrays = new(StringComparer.Ordinal)
        { "site.links", "site.sections", "site.pages" };

    private static readonly HashSet<string> ProjectsArrays = new(StringComparer.Ordinal) { "projects" };

    private static readonly HashSet<string> TagsArrays = new(StringComparer.Ordinal) { "tags" };

    private static readonly HashSet<string> NoTables = new(StringComparer.Ordinal);

    /// <summary>Parses <c>site.toml</c>, <c>projects.toml</c> and <c>tags.toml</c>.</summary>
    /// <param name="central">The central <c>.folio</c> contents.</param>
    /// <param name="sink">The build's sink.</param>
    /// <param name="config">The parsed central configuration.</param>
    /// <returns><see langword="false" /> if the build cannot continue.</returns>
    public bool TryParse(FileSet central, DiagnosticSink sink, out CentralConfig config)
    {
        config = null!;

        SiteConfig? site = ParseSite(central, sink);
        IReadOnlyList<ProjectEntry>? projects = ParseProjects(central, sink, site);
        IReadOnlyDictionary<string, TagDefinition>? tags = ParseTags(central, sink);

        if (site is null || projects is null || tags is null)
        {
            return false;
        }

        config = new CentralConfig(site, projects, tags);
        return true;
    }

    private static SiteConfig? ParseSite(FileSet central, DiagnosticSink sink)
    {
        const string path = ".folio/site.toml";

        if (!TryRead(central, path, DiagnosticCodes.CentralMissing, sink, out TomlDocumentReader? document))
        {
            return null;
        }

        DiagnosticSink file = sink.ForFile(path);

        if (!SchemaVersion.TryRead(document, file, out _))
        {
            return null;
        }

        document.ReportUnknownStructure(RootKeys, SiteTables, SiteArrays, file);

        TomlTableReader? table = document.Table("site");

        if (table is null)
        {
            file.Error(DiagnosticCodes.CentralUnparseable, "No [site] table.");
            return null;
        }

        table.ReportUnknownKeys(SiteKeys, file);

        string? url = table.String("url", file);
        string? owner = table.String("owner", file);
        string? defaultLocale = table.String("default_locale", file);
        IReadOnlyList<string> declared = table.StringArray("locales", file);

        if (!LinkTarget.IsWebUrl(url, out Uri siteUrl))
        {
            file.Error(
                DiagnosticCodes.CentralUnparseable,
                "'site.url' is required and must be an absolute http or https URL.",
                table.PositionOf("url"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(owner))
        {
            file.Error(DiagnosticCodes.CentralUnparseable, "'site.owner' is required.", table.PositionOf("owner"));
            return null;
        }

        List<LocaleTag> locales = [];

        foreach (string candidate in declared)
        {
            if (LocaleTag.TryParse(candidate, out LocaleTag locale))
            {
                locales.Add(locale);
            }
            else
            {
                file.Error(
                    DiagnosticCodes.CentralUnparseable,
                    $"'{candidate}' is not a well-formed BCP-47 locale tag.",
                    table.PositionOf("locales"));
                return null;
            }
        }

        if (locales.Count == 0)
        {
            file.Error(DiagnosticCodes.CentralUnparseable, "'site.locales' must declare at least one locale.",
                table.PositionOf("locales"));
            return null;
        }

        if (!LocaleTag.TryParse(defaultLocale, out LocaleTag fallback))
        {
            file.Error(
                DiagnosticCodes.CentralUnparseable,
                $"'site.default_locale' ({defaultLocale ?? "absent"}) is not a well-formed BCP-47 locale tag.",
                table.PositionOf("default_locale"));
            return null;
        }

        if (!locales.Contains(fallback))
        {
            file.Error(
                DiagnosticCodes.CentralDefaultLocaleUndeclared,
                $"'site.default_locale' ({fallback}) is not among the declared locales.",
                table.PositionOf("default_locale"));
            return null;
        }

        List<SiteLinkEntry> links = [];

        foreach (TomlTableReader entry in document.TableArray("site.links"))
        {
            entry.ReportUnknownKeys(LinkKeys, file);
            SiteLinkType? type = EnumNames.SiteLink(entry, "type", file);
            string? target = entry.String("url", file);

            if (type is null)
            {
                continue;
            }

            if (!LinkTarget.IsAbsolute(target ?? string.Empty, out Uri linkUrl) || !IsAllowedScheme(type.Value, linkUrl))
            {
                file.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"A site link's 'url' ({target ?? "absent"}) is not an http or https URL"
                    + $"{(type.Value is SiteLinkType.Email ? ", nor a mailto one" : string.Empty)}; it was dropped.",
                    entry.PositionOf("url"));
                continue;
            }

            links.Add(new SiteLinkEntry(type.Value, linkUrl));
        }

        IReadOnlyList<SectionEntry> sections = ReadSections(document, "site.sections", file, SectionKeys);
        IReadOnlyList<PageEntry> pages = ReadPages(document, sections, file);

        ReportUnreferencedSections(sections, pages, file);

        return new SiteConfig(siteUrl, fallback, locales, owner, links, sections, pages);
    }

    private static IReadOnlyList<ProjectEntry>? ParseProjects(FileSet central, DiagnosticSink sink, SiteConfig? site)
    {
        const string path = ".folio/projects.toml";

        if (!TryRead(central, path, DiagnosticCodes.CentralMissing, sink, out TomlDocumentReader? document))
        {
            return null;
        }

        DiagnosticSink file = sink.ForFile(path);

        if (!SchemaVersion.TryRead(document, file, out _))
        {
            return null;
        }

        document.ReportUnknownStructure(RootKeys, NoTables, ProjectsArrays, file);

        List<ProjectEntry> projects = [];

        foreach (TomlTableReader entry in document.TableArray("projects"))
        {
            entry.ReportUnknownKeys(ProjectEntryKeys, file);
            string? repo = entry.String("repo", file);

            if (string.IsNullOrWhiteSpace(repo))
            {
                file.Error(DiagnosticCodes.CentralUnparseable, "A project entry has no 'repo'.", entry.Position);
                return null;
            }

            string qualified = repo.Contains('/', StringComparison.Ordinal) || site is null
                ? repo
                : $"{site.Owner}/{repo}";

            projects.Add(new ProjectEntry(
                qualified,
                entry.String("path", file)?.Trim('/') ?? string.Empty,
                entry.String("ref", file),
                entry.Boolean("featured", file) ?? false,
                entry.Boolean("use_readme", file) ?? false));
        }

        return projects;
    }

    private static IReadOnlyDictionary<string, TagDefinition>? ParseTags(FileSet central, DiagnosticSink sink)
    {
        const string path = ".folio/tags.toml";

        if (!TryRead(central, path, DiagnosticCodes.CentralMissing, sink, out TomlDocumentReader? document))
        {
            return null;
        }

        DiagnosticSink file = sink.ForFile(path);

        if (!SchemaVersion.TryRead(document, file, out _))
        {
            return null;
        }

        document.ReportUnknownStructure(RootKeys, NoTables, TagsArrays, file);

        Dictionary<string, TagDefinition> tags = new(StringComparer.Ordinal);

        foreach (TomlTableReader entry in document.TableArray("tags"))
        {
            entry.ReportUnknownKeys(TagKeys, file);
            string? id = entry.String("id", file);

            if (string.IsNullOrWhiteSpace(id))
            {
                file.Error(DiagnosticCodes.CentralUnparseable, "A tag entry has no 'id'.", entry.Position);
                return null;
            }

            if (!tags.TryAdd(id, new TagDefinition(id, EnumNames.Kind(entry, "kind", file))))
            {
                file.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Tag id '{id}' is declared more than once; the later one was dropped.",
                    entry.Position);
            }
        }

        return tags;
    }

    /// <summary>Only <c>email</c> may be a <c>mailto:</c>; anything else is a web link.</summary>
    private static bool IsAllowedScheme(SiteLinkType type, Uri url) =>
        url.Scheme is "http" or "https" || (type is SiteLinkType.Email && url.Scheme is "mailto");

    internal static IReadOnlyList<SectionEntry> ReadSections(
        TomlDocumentReader document,
        string path,
        DiagnosticSink sink,
        IReadOnlySet<string> known)
    {
        // Only site sections are typed; a project's sections are prose by construction.
        bool typed = known.Contains("type");
        List<SectionEntry> sections = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (TomlTableReader entry in document.TableArray(path))
        {
            entry.ReportUnknownKeys(known, sink);
            string? id = entry.String("id", sink);
            string? name = entry.String("file", sink);

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    "A section entry needs both 'id' and 'file'; it was dropped.",
                    entry.Position);
                continue;
            }

            if (!seen.Add(id))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Section id '{id}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            SectionType type = SectionType.Prose;

            if (typed && entry.Has("type"))
            {
                if (EnumNames.Section(entry, "type", sink) is not { } declared)
                {
                    continue;
                }

                type = declared;
            }

            sections.Add(new SectionEntry(id, type, name));
        }

        return sections;
    }

    /// <summary>Reads the declared pages, dropping any that cannot be rendered.</summary>
    private static IReadOnlyList<PageEntry> ReadPages(
        TomlDocumentReader document,
        IReadOnlyList<SectionEntry> sections,
        DiagnosticSink sink)
    {
        HashSet<string> declared = new(sections.Select(section => section.Id), StringComparer.Ordinal);
        HashSet<Slug> seen = [];
        List<PageEntry> pages = [];
        PageEntry? home = null;

        foreach (TomlTableReader entry in document.TableArray("site.pages"))
        {
            entry.ReportUnknownKeys(PageKeys, sink);
            string? candidate = entry.String("slug", sink);

            if (!Slug.TryParse(candidate, out Slug slug))
            {
                sink.Warning(
                    DiagnosticCodes.PageSlugInvalid,
                    $"'{candidate ?? "absent"}' is not a valid page slug: lowercase letters, digits and "
                    + "hyphens only, not starting or ending with a hyphen. The page was dropped.",
                    entry.PositionOf("slug"));
                continue;
            }

            if (!seen.Add(slug))
            {
                sink.Warning(
                    DiagnosticCodes.PageDuplicateSlug,
                    $"Page slug '{slug}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            List<string> listed = [];

            foreach (string id in entry.StringArray("sections", sink))
            {
                if (!declared.Contains(id))
                {
                    sink.Warning(
                        DiagnosticCodes.PageUnknownSection,
                        $"Page '{slug}' lists section '{id}', which is not declared; it was dropped.",
                        entry.PositionOf("sections"));
                    continue;
                }

                listed.Add(id);
            }

            bool isHome = entry.Boolean("home", sink) ?? false;

            if (isHome && home is not null)
            {
                sink.Warning(
                    DiagnosticCodes.PageDuplicateHome,
                    $"Page '{slug}' is marked 'home', which page '{home.Slug}' already claims; "
                    + "the later claim was ignored.",
                    entry.PositionOf("home"));
                isHome = false;
            }

            PageEntry page = new(slug, isHome, entry.Boolean("nav", sink) ?? true, listed);
            home = isHome ? page : home;
            pages.Add(page);
        }

        if (home is null)
        {
            sink.Warning(
                DiagnosticCodes.PageNoHome,
                pages.Count == 0
                    ? "No page is declared, so the site has no entry point."
                    : "No page is marked 'home', so the site has no entry point.");
        }

        return pages;
    }

    /// <summary>Reports any declared section that no page renders.</summary>
    private static void ReportUnreferencedSections(
        IReadOnlyList<SectionEntry> sections,
        IReadOnlyList<PageEntry> pages,
        DiagnosticSink sink)
    {
        HashSet<string> referenced = new(pages.SelectMany(page => page.Sections), StringComparer.Ordinal);

        foreach (SectionEntry section in sections.Where(section => !referenced.Contains(section.Id)))
        {
            sink.Warning(
                DiagnosticCodes.SectionUnreferenced,
                $"Section '{section.Id}' is on no page, so nothing renders it.");
        }
    }

    private static bool TryRead(
        FileSet files,
        string path,
        string missingCode,
        DiagnosticSink sink,
        out TomlDocumentReader document)
    {
        document = null!;

        if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
        {
            sink.ForFile(path).Error(missingCode, $"'{path}' is missing.");
            return false;
        }

        return TomlDocumentReader.TryParse(
            contents,
            DiagnosticCodes.CentralUnparseable,
            sink.ForFile(path),
            out document);
    }
}
