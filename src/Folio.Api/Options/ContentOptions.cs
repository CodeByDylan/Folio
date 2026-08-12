using System.ComponentModel.DataAnnotations;

namespace Folio.Api.Options;

/// <summary>Where a build's inputs come from.</summary>
public enum ContentMode
{
    /// <summary>Fetched from GitHub.</summary>
    GitHub,

    /// <summary>Replayed from a recorded capture, with local working trees laid over it.</summary>
    Replay,
}

/// <summary>How content reaches a build, and which working trees stand in for a repository.</summary>
public sealed class ContentOptions : IValidatableObject
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Content";

    /// <summary>Gets where inputs come from.</summary>
    public ContentMode Mode { get; init; } = ContentMode.GitHub;

    /// <summary>
    /// Gets the working trees read instead of the capture, by <c>owner/name</c> or bare repository name.
    /// The value is the repository root holding <c>.folio</c>, absolute or relative to the content root.
    /// </summary>
    public IReadOnlyDictionary<string, string> Overlays { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves every overlay against the content root, failing on one that is not there.</summary>
    /// <param name="contentRoot">The directory relative paths are rooted at.</param>
    /// <returns>The overlays as absolute paths.</returns>
    /// <exception cref="InvalidOperationException">An overlay names a directory that does not exist.</exception>
    public IReadOnlyDictionary<string, string> Resolve(string contentRoot)
    {
        Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string repo, string directory) in Overlays)
        {
            string full = Path.GetFullPath(directory, contentRoot);

            if (!Directory.Exists(full))
            {
                throw new InvalidOperationException(
                    $"The overlay for '{repo}' points at '{full}', which does not exist.");
            }

            resolved[repo] = full;
        }

        return resolved;
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string repo, string directory) in Overlays)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                yield return new ValidationResult(
                    $"The overlay for '{repo}' names no directory.", [nameof(Overlays)]);
            }
        }
    }
}
