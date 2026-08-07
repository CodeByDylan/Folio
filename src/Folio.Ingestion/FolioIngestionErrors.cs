using Loom.Results;

namespace Folio.Ingestion;

/// <summary>The failures assembling inputs can return.</summary>
public static class FolioIngestionErrors
{
    /// <summary>The code marking a failure as caused by repository content rather than conditions.</summary>
    public const string ContentFaultCode = "folio.ingestion.content_fault";

    /// <summary>The code marking a failure as too little API budget to finish.</summary>
    public const string RateLimitInsufficientCode = "folio.ingestion.rate_limit_insufficient";

    /// <summary>The code marking a repository whose file listing exceeded GitHub's limit.</summary>
    public const string TreeTruncatedCode = "folio.ingestion.tree_truncated";

    /// <summary>The code marking the central repository itself as unreadable.</summary>
    public const string CentralUnreadableCode = "folio.ingestion.central_unreadable";

    /// <summary>The code marking the central repository as present but unusable.</summary>
    public const string CentralUnparseableCode = "folio.ingestion.central_unparseable";

    /// <summary>Creates the failure for a central repository that cannot be found.</summary>
    /// <param name="repo">The configured central repository.</param>
    /// <param name="reason">What was wrong with it.</param>
    /// <returns>An error that ends the refresh and is not a passing condition.</returns>
    public static Error CentralUnreadable(string repo, string reason) => Errors.Invalid(
        CentralUnreadableCode,
        $"The central repository '{repo}' could not be read: {reason}");

    /// <summary>Creates the failure for a central repository whose configuration cannot be used.</summary>
    /// <param name="repo">The configured central repository.</param>
    /// <param name="reason">What was wrong with it.</param>
    /// <returns>An error that ends the refresh and is not a passing condition.</returns>
    public static Error CentralUnparseable(string repo, string reason) => Errors.Invalid(
        CentralUnparseableCode,
        $"The central repository '{repo}' could not be read: {reason}");

    /// <summary>Creates the failure for a repository too large to list.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <returns>An error that drops one project.</returns>
    public static Error TreeTruncated(string repo) => Errors.Invalid(
        TreeTruncatedCode,
        $"The file listing for '{repo}' exceeded GitHub's limit, so its content cannot be read.");

    /// <summary>Creates the failure for something deterministic about a repository's content.</summary>
    /// <param name="message">What was wrong.</param>
    /// <returns>An error that drops one project.</returns>
    public static Error ContentFault(string message) =>
        Errors.Invalid(ContentFaultCode, message);

    /// <summary>Creates the failure for a fault that may not recur.</summary>
    /// <param name="exception">The failure met.</param>
    /// <returns>An error that abandons the refresh.</returns>
    public static Error Transient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The type name is safe on the anonymous endpoint; the message can carry hosts or paths.
        return Errors.Unavailable(
            "folio.ingestion.transient",
            $"GitHub could not be read ({exception.GetType().Name}).");
    }

    /// <summary>Creates the failure for too little API budget to finish a refresh.</summary>
    /// <param name="remaining">The budget left.</param>
    /// <param name="required">The budget a refresh needs to start.</param>
    /// <returns>An error that abandons the refresh before it starts.</returns>
    public static Error<int> RateLimitInsufficient(int remaining, int required) =>
        new(ErrorCategory.Unavailable,
            RateLimitInsufficientCode,
            $"Only {remaining} GitHub requests remain, below the {required} a refresh needs.",
            remaining);
}
