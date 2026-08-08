using Folio.Domain.Model;

namespace Folio.Ingestion.Snapshots;

/// <summary>The complete input set one build ran over, durable across restarts.</summary>
/// <param name="CentralRepo">The repository holding the central <c>.folio</c>, as <c>owner/name</c>.</param>
/// <param name="CentralSha">The commit the central <c>.folio</c> was read from.</param>
/// <param name="Central">The central <c>.folio</c> contents.</param>
/// <param name="Repos">One entry per listed project, in <c>projects.toml</c> order.</param>
/// <param name="CapturedAt">When these inputs were assembled.</param>
public sealed record StoredInputs(
    string CentralRepo,
    string CentralSha,
    FileSet Central,
    IReadOnlyList<RepoInput> Repos,
    DateTimeOffset CapturedAt);
