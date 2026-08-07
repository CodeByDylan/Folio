using System.ComponentModel.DataAnnotations;

namespace Folio.Api.Options;

/// <summary>How Folio reaches GitHub, and where the central config lives.</summary>
public sealed class GitHubOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "GitHub";

    /// <summary>Gets the fine-grained personal access token.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Token { get; init; }

    /// <summary>Gets the repository holding the central <c>.folio</c>, as <c>owner/name</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression(@"^[^/\s]+/[^/\s]+$", ErrorMessage = "CentralRepository must be 'owner/name'.")]
    public required string CentralRepository { get; init; }

    /// <summary>Gets the branch, tag or SHA to read the central config from.</summary>
    public string? CentralRef { get; init; }
}
