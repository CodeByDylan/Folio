using Folio.Api.Infrastructure;
using Folio.Domain.Model;

namespace Folio.Api.Features.Projects;

/// <summary>A tag with its localized label.</summary>
/// <param name="Id">The vocabulary identifier.</param>
/// <param name="Kind">What sort of thing the tag names.</param>
/// <param name="Label">The label to render.</param>
internal sealed record TagView(string Id, string? Kind, string? Label);

/// <summary>An image with its intrinsic size.</summary>
/// <param name="Role">The role it fills, such as <c>hero</c>.</param>
/// <param name="Url">The absolute, commit-pinned URL.</param>
/// <param name="Width">The intrinsic width, when it could be measured.</param>
/// <param name="Height">The intrinsic height, when it could be measured.</param>
/// <param name="Alt">The localized alt text.</param>
internal sealed record MediaView(string Role, string Url, int? Width, int? Height, string? Alt);

/// <summary>A link with its localized label.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
/// <param name="Label">The label to render.</param>
internal sealed record LinkView(string Type, string Url, string? Label);

/// <summary>A typed edge to another project.</summary>
/// <param name="Type">How the two relate.</param>
/// <param name="Target">The other project's slug.</param>
/// <param name="Label">The label to render.</param>
/// <param name="Generated">Whether this edge was inverted from one declared on the target.</param>
internal sealed record RelationView(string Type, string Target, string? Label, bool Generated);

/// <summary>One unit of authored prose.</summary>
/// <param name="Id">Stable within the project.</param>
/// <param name="Title">The section title.</param>
/// <param name="Body">The rewritten markdown body.</param>
/// <param name="Source">Whether the body was authored or lifted from a README.</param>
internal sealed record SectionView(string Id, string? Title, string? Body, string Source);

/// <summary>The GitHub facts a project carries.</summary>
/// <param name="Description">The repository description.</param>
/// <param name="Homepage">The repository homepage.</param>
/// <param name="Topics">Repository topics, alphabetical.</param>
/// <param name="PrimaryLanguage">The language GitHub reports as primary.</param>
/// <param name="Languages">The language breakdown, bytes descending.</param>
/// <param name="Stars">The stargazer count.</param>
/// <param name="Forks">The fork count.</param>
/// <param name="License">The SPDX identifier of the detected licence.</param>
/// <param name="CreatedAt">When the repository was created.</param>
/// <param name="PushedAt">When the repository was last pushed to.</param>
/// <param name="Releases">Published releases, newest first. Drafts are never included.</param>
internal sealed record MetadataView(
    string? Description,
    string? Homepage,
    IReadOnlyList<string> Topics,
    string? PrimaryLanguage,
    IReadOnlyList<LanguageView> Languages,
    int Stars,
    int Forks,
    string? License,
    DateTimeOffset CreatedAt,
    DateTimeOffset PushedAt,
    IReadOnlyList<ReleaseView> Releases);

/// <summary>One published release.</summary>
/// <param name="TagName">The release's tag.</param>
/// <param name="Name">The release's title, if set.</param>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="Url">Its page on GitHub.</param>
/// <param name="Prerelease">Whether GitHub marks it a prerelease.</param>
internal sealed record ReleaseView(
    string TagName,
    string? Name,
    DateTimeOffset PublishedAt,
    string Url,
    bool Prerelease);

/// <summary>One language and its share of the repository.</summary>
/// <param name="Language">The language name.</param>
/// <param name="Bytes">Bytes written in it.</param>
/// <param name="Percent">Its share of the total, to one decimal place.</param>
internal sealed record LanguageView(string Language, long Bytes, double Percent);

/// <summary>Maps resolved projects onto their wire shapes.</summary>
internal static class ProjectMapping
{
    /// <summary>Maps the GitHub facts.</summary>
    /// <param name="metadata">The resolved metadata.</param>
    /// <returns>The wire shape.</returns>
    public static MetadataView Metadata(RepoMetadata metadata)
    {
        return new MetadataView(
            metadata.Description,
            metadata.Homepage,
            metadata.Topics,
            metadata.PrimaryLanguage,
            [
                .. metadata.LanguageShares.Select(language => new LanguageView(
                    language.Name,
                    language.Bytes,
                    language.Percent)),
            ],
            metadata.Stars,
            metadata.Forks,
            metadata.License,
            metadata.CreatedAt,
            metadata.PushedAt,
            [
                .. metadata.Releases.Select(release => new ReleaseView(
                    release.TagName,
                    release.Name,
                    release.PublishedAt,
                    release.Url.ToString(),
                    release.IsPrerelease)),
            ]);
    }

    /// <summary>Maps a project's tags.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<TagView> Tags(ResolvedProject project, ProvenanceScope scope) =>
    [
        .. project.Tags.Select((tag, index) => new TagView(
            tag.Id,
            tag.Kind is { } kind ? Wire.Lower(kind) : null,
            scope.Take(tag.Label, $"/tags/{index}/label"))),
    ];

    /// <summary>Maps a project's links.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<LinkView> Links(ResolvedProject project, ProvenanceScope scope) =>
    [
        .. project.Links.Select((link, index) => new LinkView(
            Wire.Lower(link.Type),
            link.Url.ToString(),
            scope.Take(link.Label, $"/links/{index}/label"))),
    ];

    /// <summary>Maps a project's relations.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<RelationView> Relations(
        ResolvedProject project,
        ProvenanceScope scope) =>
    [
        .. project.Relations.Select((relation, index) => new RelationView(
            RelationVocabulary.Name(relation.Type),
            relation.Target.Value,
            scope.Take(relation.Label, $"/relations/{index}/label"),
            relation.IsGenerated)),
    ];

    /// <summary>Maps a project's media.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes, in role order.</returns>
    public static IReadOnlyList<MediaView> Media(
        ResolvedProject project,
        ProvenanceScope scope) =>
    [
        .. project.Media.Select((media, index) => new MediaView(
            media.Role,
            media.Url.ToString(),
            media.Width,
            media.Height,
            scope.Take(media.Alt, $"/media/{index}/alt"))),
    ];

    /// <summary>Maps sections.</summary>
    /// <param name="sections">The resolved sections.</param>
    /// <param name="scope">Where fallbacks are recorded.</param>
    /// <returns>The wire shapes.</returns>
    public static IReadOnlyList<SectionView> Sections(
        IReadOnlyList<ResolvedSection> sections,
        ProvenanceScope scope) =>
    [
        .. sections.Select((section, index) => new SectionView(
            section.Id,
            scope.Take(section.Title, $"/sections/{index}/title"),
            scope.Take(section.Body, $"/sections/{index}/body"),
            Wire.Lower(section.Source))),
    ];

}
