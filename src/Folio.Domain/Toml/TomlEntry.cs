using Folio.Domain.Diagnostics;
using Tomlyn.Syntax;

namespace Folio.Domain.Toml;

/// <summary>One key and its value as written in a TOML file.</summary>
/// <param name="Key">The key, with dotted segments joined by <c>.</c>.</param>
/// <param name="Value">The value node.</param>
/// <param name="Position">Where the key appears in the file.</param>
internal sealed record TomlEntry(string Key, ValueSyntax Value, SourcePosition Position);
