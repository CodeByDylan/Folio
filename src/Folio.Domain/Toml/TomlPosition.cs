using Folio.Domain.Diagnostics;
using Tomlyn.Syntax;

namespace Folio.Domain.Toml;

/// <summary>Converts Tomlyn's zero-based positions to one-based ones.</summary>
internal static class TomlPosition
{
    /// <summary>Gets the start of a syntax node as a one-based position.</summary>
    /// <param name="node">The node to locate.</param>
    /// <returns>The node's start position.</returns>
    public static SourcePosition Of(SyntaxNode node) => Of(node.Span);

    /// <summary>Gets the start of a span as a one-based position.</summary>
    /// <param name="span">The span to locate.</param>
    /// <returns>The span's start position.</returns>
    public static SourcePosition Of(SourceSpan span) => new(span.Start.Line + 1, span.Start.Column + 1);
}
