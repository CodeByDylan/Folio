using System.Text;
using Folio.Domain.Diagnostics;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Folio.Domain.Toml;

/// <summary>A parsed TOML file, indexed by table path.</summary>
internal sealed class TomlDocumentReader
{
    // The shared UTF8 encoding substitutes U+FFFD; a config file that is not UTF-8 is an error.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Dictionary<string, TomlTableReader> _tables;
    private readonly Dictionary<string, List<TomlTableReader>> _tableArrays;

    private TomlDocumentReader(
        TomlTableReader root,
        Dictionary<string, TomlTableReader> tables,
        Dictionary<string, List<TomlTableReader>> tableArrays)
    {
        Root = root;
        _tables = tables;
        _tableArrays = tableArrays;
    }

    /// <summary>Gets the keys declared before any table header.</summary>
    public TomlTableReader Root { get; }

    /// <summary>Gets every declared table, in source order.</summary>
    public IEnumerable<TomlTableReader> Tables =>
        _tables.Values.OrderBy(table => table.Position.Line).ThenBy(table => table.Position.Column);

    /// <summary>Parses a file, reporting a syntax error under the given code.</summary>
    /// <param name="contents">The file's bytes, expected to be UTF-8.</param>
    /// <param name="unparseableCode">The diagnostic code to report a syntax error under.</param>
    /// <param name="sink">A sink already scoped to this file.</param>
    /// <param name="document">The parsed document.</param>
    /// <returns><see langword="true" /> if the file parsed without errors.</returns>
    public static bool TryParse(
        ReadOnlyMemory<byte> contents,
        string unparseableCode,
        DiagnosticSink sink,
        out TomlDocumentReader document)
    {
        document = null!;

        string text;

        try
        {
            text = Utf8.GetString(contents.Span);
        }
        catch (DecoderFallbackException)
        {
            sink.Error(unparseableCode, "The file is not valid UTF-8.");
            return false;
        }

        DocumentSyntax syntax;

        try
        {
            syntax = SyntaxParser.Parse(text, sourceName: string.Empty, validate: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tomlyn throws past its nesting depth rather than reporting it; that is a broken file.
            sink.Error(unparseableCode, $"The file could not be parsed: {exception.Message}");
            return false;
        }

        if (syntax.HasErrors)
        {
            for (int i = 0; i < syntax.Diagnostics.Count; i++)
            {
                DiagnosticMessage message = syntax.Diagnostics[i];

                if (message.Kind is DiagnosticMessageKind.Error)
                {
                    sink.Error(unparseableCode, message.Message, TomlPosition.Of(message.Span));
                }
            }

            return false;
        }

        document = Index(syntax);
        return true;
    }

    /// <summary>Reports every root key, table and table array the schema does not define.</summary>
    /// <param name="rootKeys">The keys the schema defines before any table header.</param>
    /// <param name="tables">The table paths the schema defines.</param>
    /// <param name="tableArrays">The table-array paths the schema defines.</param>
    /// <param name="sink">A sink scoped to the file.</param>
    public void ReportUnknownStructure(
        IReadOnlySet<string> rootKeys,
        IReadOnlySet<string> tables,
        IReadOnlySet<string> tableArrays,
        DiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        Root.ReportUnknownKeys(rootKeys, sink);

        IEnumerable<(string Path, TomlTableReader Reader, bool IsArray)> declared =
        [
            .. _tables.Select(entry => (entry.Key, entry.Value, IsArray: false)),
            .. _tableArrays.SelectMany(entry => entry.Value.Select(reader => (entry.Key, reader, IsArray: true))),
        ];

        foreach ((string path, TomlTableReader reader, bool isArray) in Ordered(declared))
        {
            if ((isArray ? tableArrays : tables).Contains(path))
            {
                continue;
            }

            sink.Warning(
                DiagnosticCodes.SchemaUnknownKey,
                isArray ? $"Unknown table '[[{path}]]' was ignored." : $"Unknown table '[{path}]' was ignored.",
                reader.Position);
        }
    }

    private static IEnumerable<(string Path, TomlTableReader Reader, bool IsArray)> Ordered(
        IEnumerable<(string Path, TomlTableReader Reader, bool IsArray)> entries) =>
        entries.OrderBy(entry => entry.Reader.Position.Line).ThenBy(entry => entry.Reader.Position.Column);

    /// <summary>Gets a table by its dotted path.</summary>
    /// <param name="path">The dotted table path, such as <c>project.media</c>.</param>
    /// <returns>The table, or <see langword="null" /> if it is not declared.</returns>
    public TomlTableReader? Table(string path) => _tables.GetValueOrDefault(path);

    /// <summary>Gets the entries of a table array by its dotted path.</summary>
    /// <param name="path">The dotted path, such as <c>projects</c>.</param>
    /// <returns>The entries in declaration order, empty if none are declared.</returns>
    public IReadOnlyList<TomlTableReader> TableArray(string path) =>
        _tableArrays.TryGetValue(path, out List<TomlTableReader>? entries) ? entries : [];

    private static TomlDocumentReader Index(DocumentSyntax syntax)
    {
        Dictionary<string, TomlTableReader> tables = new(StringComparer.Ordinal);
        Dictionary<string, List<TomlTableReader>> tableArrays = new(StringComparer.Ordinal);

        TomlTableReader root = new(
            path: string.Empty,
            position: new SourcePosition(1, 1),
            entries: ReadEntries(syntax.KeyValues));

        foreach (TableSyntaxBase table in syntax.Tables)
        {
            string path = KeyPath(table.Name);
            TomlTableReader reader = new(path, TomlPosition.Of(table), ReadEntries(table.Items));

            if (table is TableArraySyntax)
            {
                if (!tableArrays.TryGetValue(path, out List<TomlTableReader>? entries))
                {
                    entries = [];
                    tableArrays[path] = entries;
                }

                entries.Add(reader);
            }
            else
            {
                tables[path] = reader;
            }
        }

        return new TomlDocumentReader(root, tables, tableArrays);
    }

    private static List<TomlEntry> ReadEntries(SyntaxList<KeyValueSyntax> keyValues)
    {
        List<TomlEntry> entries = [];

        foreach (KeyValueSyntax keyValue in keyValues)
        {
            if (keyValue.Key is null || keyValue.Value is null)
            {
                continue;
            }

            entries.Add(new TomlEntry(KeyPath(keyValue.Key), keyValue.Value, TomlPosition.Of(keyValue)));
        }

        return entries;
    }

    private static string KeyPath(KeySyntax? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        StringBuilder path = new(KeyText(key.Key));

        foreach (DottedKeyItemSyntax dotted in key.DotKeys)
        {
            _ = path.Append('.').Append(KeyText(dotted.Key));
        }

        return path.ToString();
    }

    private static string KeyText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
        StringValueSyntax text => text.Value ?? string.Empty,
        _ => string.Empty,
    };
}
