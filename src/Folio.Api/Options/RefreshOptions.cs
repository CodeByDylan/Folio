using System.ComponentModel.DataAnnotations;

namespace Folio.Api.Options;

/// <summary>How often the portfolio is rebuilt, and the limits a rebuild runs under.</summary>
public sealed class RefreshOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Refresh";

    /// <summary>Gets how long to wait between rebuilds.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets how long one rebuild may take before it is abandoned.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets how many GitHub requests may be in flight at once.</summary>
    [Range(1, 32)]
    public int FetchConcurrency { get; init; } = 6;

    /// <summary>Gets the remaining request budget below which a rebuild will not start.</summary>
    [Range(0, 5000)]
    public int MinimumRateLimitBudget { get; init; } = 500;

    /// <summary>Gets the largest single file that will be fetched from a repository.</summary>
    [Range(1024, 104_857_600)]
    public int MaxFileBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Gets the most files that will be fetched from one repository.</summary>
    [Range(1, 100_000)]
    public int MaxFileCount { get; init; } = 2000;

    /// <summary>Gets the most bytes that will be fetched from one repository.</summary>
    [Range(1024, 1_073_741_824)]
    public long MaxTotalBytes { get; init; } = 64 * 1024 * 1024;
}
