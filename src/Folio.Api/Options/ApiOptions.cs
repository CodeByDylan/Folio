using System.ComponentModel.DataAnnotations;

namespace Folio.Api.Options;

/// <summary>The HTTP surface's own settings.</summary>
public sealed class ApiOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Api";

    /// <summary>Gets the key that authorizes a manual refresh.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32)]
    public required string RefreshKey { get; init; }

    /// <summary>Gets the origins permitted by CORS.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Gets the length of the rate-limiting window.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets how many requests one client may make per window.</summary>
    [Range(1, 10_000)]
    public int RateLimitPermits { get; init; } = 120;

    /// <summary>Gets how long a client may reuse a content response before revalidating.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan CacheMaxAge { get; init; } = TimeSpan.FromSeconds(60);
}
