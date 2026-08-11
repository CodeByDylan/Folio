namespace Folio.Domain.Model;

/// <summary>One project, resolved for one locale.</summary>
/// <param name="Slug">The project's stable identity.</param>
/// <param name="Repo">The <c>owner/name</c> it was read from.</param>
/// <param name="PinnedSha">The commit its content and media were read from.</param>
/// <param name="IsFeatured">Whether the site highlights it.</param>
/// <param name="Name">The project name.</param>
/// <param name="Tagline">The project tagline.</param>
/// <param name="Status">How active it is.</param>
/// <param name="Role">The part played in it.</param>
/// <param name="Started">When work began, as <c>YYYY</c> or <c>YYYY-MM</c>.</param>
/// <param name="Ended">When work concluded. Absent means ongoing.</param>
/// <param name="Tags">Applied tags, resolved against the central vocabulary.</param>
/// <param name="Links">Project links, in declaration order.</param>
/// <param name="Relations">Declared and generated relations.</param>
/// <param name="Media">Named media, ordered by role.</param>
/// <param name="Sections">Authored prose, in declaration order.</param>
/// <param name="Metadata">The derived GitHub facts, deterministically ordered.</param>
public sealed record ResolvedProject(
    Slug Slug,
    string Repo,
    string PinnedSha,
    bool IsFeatured,
    Localized<string>? Name,
    Localized<string>? Tagline,
    ProjectStatus? Status,
    ProjectRole? Role,
    string? Started,
    string? Ended,
    IReadOnlyList<ResolvedTag> Tags,
    IReadOnlyList<ResolvedLink> Links,
    IReadOnlyList<ResolvedRelation> Relations,
    IReadOnlyList<ResolvedMedia> Media,
    IReadOnlyList<ResolvedProseSection> Sections,
    RepoMetadata Metadata);

/// <summary>A tag from the central vocabulary with its localized label.</summary>
/// <param name="Id">The vocabulary identifier.</param>
/// <param name="Kind">What sort of thing the tag names.</param>
/// <param name="Label">The label to render.</param>
public sealed record ResolvedTag(string Id, TagKind? Kind, Localized<string>? Label);

/// <summary>A project link with its localized label.</summary>
/// <param name="Type">What the link points at.</param>
/// <param name="Url">The link target.</param>
/// <param name="Label">The label to render, if the vocabulary supplies one.</param>
public sealed record ResolvedLink(LinkType Type, Uri Url, Localized<string>? Label);

/// <summary>A typed edge to another project.</summary>
/// <param name="Type">How the two relate.</param>
/// <param name="Target">The other project's slug.</param>
/// <param name="Label">The label to render, from the central vocabulary.</param>
/// <param name="IsGenerated">Whether this edge was inverted from one declared on the target.</param>
public sealed record ResolvedRelation(RelationType Type, Slug Target, Localized<string>? Label, bool IsGenerated);

/// <summary>An image, with intrinsic dimensions where they could be read.</summary>
/// <param name="Role">The role it fills, such as <c>hero</c>.</param>
/// <param name="Url">An absolute URL, pinned to a commit when the media lives in the repository.</param>
/// <param name="Width">The intrinsic width, if measured.</param>
/// <param name="Height">The intrinsic height, if measured.</param>
/// <param name="Alt">The localized alt text.</param>
public sealed record ResolvedMedia(string Role, Uri Url, int? Width, int? Height, Localized<string>? Alt);
