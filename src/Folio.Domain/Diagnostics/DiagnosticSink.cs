namespace Folio.Domain.Diagnostics;

/// <summary>Collects diagnostics during one resolution, stamping context onto each.</summary>
public sealed class DiagnosticSink
{
    private readonly List<Diagnostic> _buffer;
    private readonly string? _project;
    private readonly string? _file;

    /// <summary>Creates an empty sink.</summary>
    public DiagnosticSink()
        : this([], project: null, file: null)
    {
    }

    private DiagnosticSink(List<Diagnostic> buffer, string? project, string? file)
    {
        _buffer = buffer;
        _project = project;
        _file = file;
    }

    /// <summary>Gets everything written so far, in emission order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _buffer;

    /// <summary>Returns a sink that stamps every diagnostic with this project.</summary>
    /// <param name="project">The slug of the project being resolved.</param>
    /// <returns>A sink sharing this one's buffer.</returns>
    public DiagnosticSink ForProject(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        return new DiagnosticSink(_buffer, project, _file);
    }

    /// <summary>Returns a sink that stamps every diagnostic with this file.</summary>
    /// <param name="file">A repo-relative path.</param>
    /// <returns>A sink sharing this one's buffer.</returns>
    public DiagnosticSink ForFile(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return new DiagnosticSink(_buffer, _project, file);
    }

    /// <summary>Records that something was dropped.</summary>
    /// <param name="code">A code from <see cref="DiagnosticCodes" />.</param>
    /// <param name="message">A human-readable description.</param>
    /// <param name="position">Where in the current file the condition was found.</param>
    /// <param name="pointer">An RFC 6901 pointer to the response field concerned.</param>
    public void Error(string code, string message, SourcePosition? position = null, string? pointer = null) =>
        Write(code, DiagnosticSeverity.Error, message, position, pointer);

    /// <summary>Records that something was ignored or substituted.</summary>
    /// <param name="code">A code from <see cref="DiagnosticCodes" />.</param>
    /// <param name="message">A human-readable description.</param>
    /// <param name="position">Where in the current file the condition was found.</param>
    /// <param name="pointer">An RFC 6901 pointer to the response field concerned.</param>
    public void Warning(string code, string message, SourcePosition? position = null, string? pointer = null) =>
        Write(code, DiagnosticSeverity.Warning, message, position, pointer);

    /// <summary>Records an expected fallback or coverage fact.</summary>
    /// <param name="code">A code from <see cref="DiagnosticCodes" />.</param>
    /// <param name="message">A human-readable description.</param>
    /// <param name="position">Where in the current file the condition was found.</param>
    /// <param name="pointer">An RFC 6901 pointer to the response field concerned.</param>
    public void Info(string code, string message, SourcePosition? position = null, string? pointer = null) =>
        Write(code, DiagnosticSeverity.Info, message, position, pointer);

    private void Write(
        string code,
        DiagnosticSeverity severity,
        string message,
        SourcePosition? position,
        string? pointer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!DiagnosticCodes.All.Contains(code))
        {
            throw new ArgumentException($"'{code}' is not a declared diagnostic code.", nameof(code));
        }

        _buffer.Add(new Diagnostic(code, severity, message, _project, _file, position, pointer));
    }
}
