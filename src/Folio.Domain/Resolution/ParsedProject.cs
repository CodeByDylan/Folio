using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Localization;
using Folio.Domain.Model;

namespace Folio.Domain.Resolution;

/// <summary>One project after parsing, before any locale has been resolved.</summary>
/// <param name="Slug">The resolved slug.</param>
/// <param name="Input">The repository input it came from.</param>
/// <param name="FolioRoot">The path of its <c>.folio</c> directory.</param>
/// <param name="Entry">Its entry in <c>projects.toml</c>.</param>
/// <param name="Config">Its parsed configuration.</param>
/// <param name="Locales">Its locale bundles, keyed by locale.</param>
/// <param name="Sink">The diagnostics found while parsing it.</param>
internal sealed record ParsedProject(
    Slug Slug,
    RepoInput Input,
    string FolioRoot,
    ProjectEntry Entry,
    ProjectConfig Config,
    IReadOnlyDictionary<LocaleTag, LocaleBundle> Locales,
    DiagnosticSink Sink);
