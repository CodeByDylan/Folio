using Folio.Domain.Diagnostics;
using Loom.Results;

namespace Folio.Domain;

/// <summary>The failures resolution can return.</summary>
public static class FolioErrors
{
    /// <summary>Creates the failure returned when the central configuration cannot be read.</summary>
    /// <param name="diagnostics">What the resolver found before it gave up.</param>
    /// <returns>An error carrying those diagnostics.</returns>
    public static Error<IReadOnlyList<Diagnostic>> CentralConfigInvalid(IReadOnlyList<Diagnostic> diagnostics) =>
        new(ErrorCategory.Invalid,
            "folio.central_config_invalid",
            "The central configuration could not be read, so no portfolio was produced.",
            diagnostics);
}
