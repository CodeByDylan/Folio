namespace Folio.Domain.Diagnostics;

/// <summary>How much a diagnostic cost.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Fallbacks and coverage.</summary>
    Info = 0,

    /// <summary>Something was ignored or substituted.</summary>
    Warning = 1,

    /// <summary>Something was dropped: a project, a file, or the whole build.</summary>
    Error = 2,
}
