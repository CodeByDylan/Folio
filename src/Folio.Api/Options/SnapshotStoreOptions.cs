using System.ComponentModel.DataAnnotations;

namespace Folio.Api.Options;

/// <summary>Where the raw inputs of the last successful build are kept.</summary>
public sealed class SnapshotStoreOptions : IValidatableObject
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "SnapshotStore";

    /// <summary>Gets which store to use.</summary>
    public SnapshotStoreMode Mode { get; init; } = SnapshotStoreMode.File;

    /// <summary>Gets the path the file store writes to.</summary>
    public string FilePath { get; init; } = "folio-inputs.json";

    /// <summary>Gets the Redis connection string. Required when <see cref="Mode" /> is <see cref="SnapshotStoreMode.Redis" />.</summary>
    public string? RedisConnectionString { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Mode is SnapshotStoreMode.Redis && string.IsNullOrWhiteSpace(RedisConnectionString))
        {
            yield return new ValidationResult(
                "RedisConnectionString is required when Mode is Redis.",
                [nameof(RedisConnectionString)]);
        }

        if (Mode is SnapshotStoreMode.File && string.IsNullOrWhiteSpace(FilePath))
        {
            yield return new ValidationResult("FilePath is required when Mode is File.", [nameof(FilePath)]);
        }
    }
}

/// <summary>Which snapshot store implementation to resolve.</summary>
public enum SnapshotStoreMode
{
    /// <summary>Write to a local file.</summary>
    File,

    /// <summary>Write to Redis.</summary>
    Redis,
}
