namespace Folio.Domain.Model;

/// <summary>How active a project is. Only <see cref="Archived" /> is derivable from GitHub.</summary>
public enum ProjectStatus
{
    /// <summary>Under initial development.</summary>
    Wip,

    /// <summary>Actively developed.</summary>
    Active,

    /// <summary>Kept working, not extended.</summary>
    Maintenance,

    /// <summary>No longer worked on.</summary>
    Archived,
}

/// <summary>The part played in a project.</summary>
public enum ProjectRole
{
    /// <summary>Wrote it.</summary>
    Author,

    /// <summary>Maintains it.</summary>
    Maintainer,

    /// <summary>Contributed to it.</summary>
    Contributor,
}

/// <summary>What a project link points at.</summary>
public enum LinkType
{
    /// <summary>A live deployment.</summary>
    Demo,

    /// <summary>Documentation.</summary>
    Docs,

    /// <summary>A published package.</summary>
    Package,

    /// <summary>Writing about the project.</summary>
    Article,

    /// <summary>Design work.</summary>
    Design,
}

/// <summary>What a site link points at.</summary>
public enum SiteLinkType
{
    /// <summary>A GitHub profile.</summary>
    GitHub,

    /// <summary>A LinkedIn profile.</summary>
    LinkedIn,

    /// <summary>A Mastodon profile.</summary>
    Mastodon,

    /// <summary>An email address, as a <c>mailto:</c> URL.</summary>
    Email,

    /// <summary>Any other site.</summary>
    Website,
}

/// <summary>How two projects relate.</summary>
public enum RelationType
{
    /// <summary>Depends on the target.</summary>
    Uses,

    /// <summary>Is used by the target. Generated from <see cref="Uses" />.</summary>
    UsedBy,

    /// <summary>Is a component of the target.</summary>
    PartOf,

    /// <summary>Contains the target. Generated from <see cref="PartOf" />.</summary>
    Contains,

    /// <summary>Replaces the target.</summary>
    SuccessorOf,

    /// <summary>Was replaced by the target. Generated from <see cref="SuccessorOf" />.</summary>
    PredecessorOf,

    /// <summary>Belongs alongside the target.</summary>
    Companion,
}

/// <summary>What sort of thing a tag names.</summary>
public enum TagKind
{
    /// <summary>A programming language.</summary>
    Language,

    /// <summary>A framework or library.</summary>
    Framework,

    /// <summary>A problem domain.</summary>
    Domain,

    /// <summary>A tool.</summary>
    Tool,
}

/// <summary>What a section holds, and so what renders it.</summary>
public enum SectionType
{
    /// <summary>Authored markdown.</summary>
    Prose,

    /// <summary>A headline, a subheadline, calls to action and an optional portrait.</summary>
    Hero,
}

/// <summary>Where a section's body was authored.</summary>
public enum SectionSource
{
    /// <summary>Written in <c>.folio</c>.</summary>
    Folio,

    /// <summary>Lifted from the repository README, so its fallback is permanent rather than missing.</summary>
    Readme,
}
