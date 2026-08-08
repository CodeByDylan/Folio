namespace Folio.Domain.Diagnostics;

/// <summary>Something the resolver found while reading a configuration or content file.</summary>
/// <param name="Code">A stable identifier from <see cref="DiagnosticCodes" />.</param>
/// <param name="Severity">How much the condition cost.</param>
/// <param name="Message">A human-readable description. Not stable; discriminate on <paramref name="Code" />.</param>
/// <param name="Project">The slug of the project responsible.</param>
/// <param name="File">The repo-relative path of the file responsible.</param>
/// <param name="Position">Where in <paramref name="File" /> the condition was found.</param>
/// <param name="Pointer">An RFC 6901 pointer to the response field concerned.</param>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Project = null,
    string? File = null,
    SourcePosition? Position = null,
    string? Pointer = null);
