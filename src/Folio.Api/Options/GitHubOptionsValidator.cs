using Microsoft.Extensions.Options;

namespace Folio.Api.Options;

/// <summary>Requires a token only when content is actually fetched from GitHub.</summary>
/// <param name="content">How content reaches a build.</param>
public sealed class GitHubOptionsValidator(IOptions<ContentOptions> content) : IValidateOptions<GitHubOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (content.Value.Mode is ContentMode.Replay || !string.IsNullOrWhiteSpace(options.Token))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail("GitHub:Token is required when Content:Mode is GitHub.");
    }
}
