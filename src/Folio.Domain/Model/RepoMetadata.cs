namespace Folio.Domain.Model;

/// <summary>The facts GitHub supplies about a repository, none of which need authoring.</summary>
/// <param name="Owner">The repository owner.</param>
/// <param name="Name">The repository name.</param>
/// <param name="Description">The repository description, if set.</param>
/// <param name="Homepage">The repository homepage, if set.</param>
/// <param name="Topics">The repository topics, ordered alphabetically before use.</param>
/// <param name="PrimaryLanguage">The language GitHub reports as primary, if any.</param>
/// <param name="Languages">The language breakdown, bytes descending then alphabetical.</param>
/// <param name="Stars">The stargazer count.</param>
/// <param name="Forks">The fork count.</param>
/// <param name="License">The SPDX identifier of the detected licence, if any.</param>
/// <param name="IsArchived">Whether GitHub reports the repository as archived.</param>
/// <param name="CreatedAt">When the repository was created.</param>
/// <param name="PushedAt">When the repository was last pushed to.</param>
/// <param name="Releases">Published releases, newest first.</param>
public sealed record RepoMetadata(
    string Owner,
    string Name,
    string? Description,
    string? Homepage,
    IReadOnlyList<string> Topics,
    string? PrimaryLanguage,
    IReadOnlyList<RepoLanguage> Languages,
    int Stars,
    int Forks,
    string? License,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset PushedAt,
    IReadOnlyList<RepoRelease> Releases)
{
    /// <summary>Gets the language breakdown with each language's share of the total.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<LanguageShare> LanguageShares
    {
        get
        {
            long total = Languages.Sum(language => language.Bytes);

            return
            [
                .. Languages.Select(language => new LanguageShare(
                    language.Name,
                    language.Bytes,
                    total == 0 ? 0 : Math.Round(language.Bytes * 100d / total, 1))),
            ];
        }
    }
}

/// <summary>One language and how many bytes are written in it.</summary>
/// <param name="Name">The language name.</param>
/// <param name="Bytes">Bytes written in it.</param>
public sealed record RepoLanguage(string Name, long Bytes);

/// <summary>One language and its share of the repository, to one decimal place.</summary>
/// <param name="Name">The language name.</param>
/// <param name="Bytes">Bytes written in it.</param>
/// <param name="Percent">Its share of the total.</param>
public sealed record LanguageShare(string Name, long Bytes, double Percent);

/// <summary>One published release.</summary>
/// <param name="TagName">The release's tag.</param>
/// <param name="Name">The release's title, if set.</param>
/// <param name="PublishedAt">When the release was published.</param>
/// <param name="Url">The release's page on GitHub.</param>
/// <param name="IsPrerelease">Whether GitHub marks it a prerelease; reported, never filtered on.</param>
public sealed record RepoRelease(
    string TagName,
    string? Name,
    DateTimeOffset PublishedAt,
    Uri Url,
    bool IsPrerelease);
