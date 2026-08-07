using System.Collections.Frozen;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Localization;

/// <summary>The strings declared for one locale.</summary>
internal sealed class LocaleBundle
{
    private readonly FrozenDictionary<string, string> _strings;

    private LocaleBundle(LocaleTag locale, FrozenDictionary<string, string> strings)
    {
        Locale = locale;
        _strings = strings;
    }

    /// <summary>Gets the locale these strings belong to.</summary>
    public LocaleTag Locale { get; }

    /// <summary>Gets every declared key.</summary>
    public IReadOnlyCollection<string> Keys => _strings.Keys;

    /// <summary>Reads every <c>locales/&lt;locale&gt;.toml</c> the file set declares.</summary>
    /// <param name="files">The file set to read from.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="locales">The locales the site declares.</param>
    /// <param name="sink">A sink scoped to the owning project, if any.</param>
    /// <returns>One bundle per locale that has a file, keyed by locale.</returns>
    public static IReadOnlyDictionary<LocaleTag, LocaleBundle> ReadAll(
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> locales,
        DiagnosticSink sink)
    {
        Dictionary<LocaleTag, LocaleBundle> bundles = [];

        ReportUndeclaredFiles(files, folioRoot, locales, sink);

        foreach (LocaleTag locale in locales)
        {
            string path = $"{folioRoot}/locales/{locale.Value}.toml";

            if (!files.TryGet(path, out ReadOnlyMemory<byte> contents))
            {
                continue;
            }

            DiagnosticSink file = sink.ForFile(path);

            if (!TomlDocumentReader.TryParse(contents, DiagnosticCodes.LocaleUnparseable, file, out TomlDocumentReader document))
            {
                continue;
            }

            Dictionary<string, string> strings = new(StringComparer.Ordinal);

            foreach (TomlEntry entry in document.Root.Entries.OrderBy(entry => entry.Position.Line).ThenBy(entry => entry.Position.Column))
            {
                string? value = document.Root.String(entry.Key, file);

                if (value is not null)
                {
                    strings[entry.Key] = value;
                }
            }

            // [project] with tagline is the same declaration as project.tagline, so both forms load.
            foreach (TomlTableReader table in document.Tables)
            {
                foreach (TomlEntry entry in table.Entries.OrderBy(entry => entry.Position.Line).ThenBy(entry => entry.Position.Column))
                {
                    string? value = table.String(entry.Key, file);

                    if (value is not null)
                    {
                        strings[$"{table.Path}.{entry.Key}"] = value;
                    }
                }
            }

            bundles[locale] = new LocaleBundle(locale, strings.ToFrozenDictionary(StringComparer.Ordinal));
        }

        return bundles;
    }

    private static void ReportUndeclaredFiles(
        FileSet files,
        string folioRoot,
        IReadOnlyList<LocaleTag> locales,
        DiagnosticSink sink)
    {
        string prefix = $"{folioRoot}/locales/";

        foreach (string path in files.Under($"{folioRoot}/locales"))
        {
            string name = path[prefix.Length..];

            if (IsDeclared(name, locales))
            {
                continue;
            }

            sink.ForFile(path).Warning(
                DiagnosticCodes.LocaleFileUndeclared,
                $"'{name}' is not a declared locale's '<locale>.toml'; the file was ignored.");
        }
    }

    private static bool IsDeclared(string name, IReadOnlyList<LocaleTag> locales)
    {
        if (name.Contains('/', StringComparison.Ordinal) || !name.EndsWith(".toml", StringComparison.Ordinal))
        {
            return false;
        }

        string tag = name[..^".toml".Length];

        return LocaleTag.TryParse(tag, out LocaleTag locale)
            && locales.Contains(locale)
            && string.Equals(locale.Value, tag, StringComparison.Ordinal);
    }

    /// <summary>Gets a string.</summary>
    /// <param name="key">The dotted key.</param>
    /// <param name="value">The declared string.</param>
    /// <returns><see langword="true" /> if the key is declared.</returns>
    public bool TryGet(string key, out string value) => _strings.TryGetValue(key, out value!);
}
