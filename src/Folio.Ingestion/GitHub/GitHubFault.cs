using System.Net;
using Octokit;

namespace Folio.Ingestion.GitHub;

/// <summary>Whether a failure is caused by repository content or by conditions that pass.</summary>
public enum GitHubFaultKind
{
    /// <summary>Deterministic and caused by what is in a repository.</summary>
    Content,

    /// <summary>Not caused by content.</summary>
    Transient,
}

/// <summary>Classifies a failed GitHub call.</summary>
public static class GitHubFault
{
    /// <summary>Classifies an exception.</summary>
    /// <param name="exception">The failure to classify.</param>
    /// <returns>Whether it is a content or a transient fault.</returns>
    public static GitHubFaultKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is NotFoundException ? GitHubFaultKind.Content : GitHubFaultKind.Transient;
    }
}
