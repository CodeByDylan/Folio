namespace Folio.Domain.Diagnostics;

/// <summary>A one-based location within a source file.</summary>
/// <param name="Line">The one-based line number.</param>
/// <param name="Column">The one-based column number.</param>
public sealed record SourcePosition(int Line, int Column);
