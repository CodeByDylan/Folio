using System.Collections.Frozen;
using Folio.Domain.Diagnostics;
using Tomlyn.Syntax;

namespace Folio.Domain.Toml;

/// <summary>Reads typed values out of one TOML table, reporting anything malformed.</summary>
internal sealed class TomlTableReader
{
    private readonly FrozenDictionary<string, TomlEntry> _entries;

    internal TomlTableReader(string path, SourcePosition position, IEnumerable<TomlEntry> entries)
    {
        Path = path;
        Position = position;
        _entries = entries.ToFrozenDictionary(entry => entry.Key, StringComparer.Ordinal);
    }

    /// <summary>Gets the dotted path of this table, empty at the document root.</summary>
    public string Path { get; }

    /// <summary>Gets where the table header appears in the file.</summary>
    public SourcePosition Position { get; }

    /// <summary>Gets every entry declared directly in this table.</summary>
    public IEnumerable<TomlEntry> Entries => _entries.Values;

    /// <summary>Gets whether a key is present.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is declared.</returns>
    public bool Has(string key) => _entries.ContainsKey(key);

    /// <summary>Gets where a key appears, or the table header if it is absent.</summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>The position to attach a diagnostic to.</returns>
    public SourcePosition PositionOf(string key) =>
        _entries.TryGetValue(key, out TomlEntry? entry) ? entry.Position : Position;

    /// <summary>Reads a string, reporting a type mismatch.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report a mismatch.</param>
    /// <returns>The string, or <see langword="null" /> if absent or not a string.</returns>
    public string? String(string key, DiagnosticSink sink)
    {
        if (!_entries.TryGetValue(key, out TomlEntry? entry))
        {
            return null;
        }

        if (entry.Value is StringValueSyntax text)
        {
            return text.Value;
        }

        ReportKind(key, "a string", entry, sink);
        return null;
    }

    /// <summary>Reads a boolean, reporting a type mismatch.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report a mismatch.</param>
    /// <returns>The boolean, or <see langword="null" /> if absent or not a boolean.</returns>
    public bool? Boolean(string key, DiagnosticSink sink)
    {
        if (!_entries.TryGetValue(key, out TomlEntry? entry))
        {
            return null;
        }

        if (entry.Value is BooleanValueSyntax flag)
        {
            return flag.Value;
        }

        ReportKind(key, "a boolean", entry, sink);
        return null;
    }

    /// <summary>Reads an integer, reporting a type mismatch.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report a mismatch.</param>
    /// <returns>The integer, or <see langword="null" /> if absent or not an integer.</returns>
    public long? Integer(string key, DiagnosticSink sink)
    {
        if (!_entries.TryGetValue(key, out TomlEntry? entry))
        {
            return null;
        }

        if (entry.Value is IntegerValueSyntax number)
        {
            return number.Value;
        }

        ReportKind(key, "an integer", entry, sink);
        return null;
    }

    /// <summary>Reads an array of strings, reporting any element that is not a string.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="sink">Where to report a mismatch.</param>
    /// <returns>The strings, empty if absent.</returns>
    public IReadOnlyList<string> StringArray(string key, DiagnosticSink sink)
    {
        if (!_entries.TryGetValue(key, out TomlEntry? entry))
        {
            return [];
        }

        if (entry.Value is not ArraySyntax array)
        {
            ReportKind(key, "an array of strings", entry, sink);
            return [];
        }

        List<string> values = [];

        foreach (ArrayItemSyntax item in array.Items)
        {
            if (item.Value is StringValueSyntax { Value: not null } text)
            {
                values.Add(text.Value);
            }
            else
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"'{Qualify(key)}' contains a non-string element, which was dropped.",
                    TomlPosition.Of(item));
            }
        }

        return values;
    }

    /// <summary>Reports every key not in the known set.</summary>
    /// <param name="known">The keys the schema defines for this table.</param>
    /// <param name="sink">Where to report unknown keys.</param>
    public void ReportUnknownKeys(IReadOnlySet<string> known, DiagnosticSink sink)
    {
        foreach (TomlEntry entry in _entries.Values.OrderBy(entry => entry.Position.Line).ThenBy(entry => entry.Position.Column))
        {
            if (!known.Contains(entry.Key))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaUnknownKey,
                    $"Unknown key '{Qualify(entry.Key)}' was ignored.",
                    entry.Position);
            }
        }
    }

    private void ReportKind(string key, string expected, TomlEntry entry, DiagnosticSink sink) =>
        sink.Warning(
            DiagnosticCodes.SchemaInvalidValue,
            $"'{Qualify(key)}' is not {expected} and was ignored.",
            entry.Position);

    private string Qualify(string key) => Path.Length == 0 ? key : $"{Path}.{key}";
}
