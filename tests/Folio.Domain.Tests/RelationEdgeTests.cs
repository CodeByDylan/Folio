using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class RelationEdgeTests
{
    [Test]
    public async Task Mutual_Companions_Produce_A_Single_Edge()
    {
        Snapshot snapshot = Resolve(
            ("a", "companion", "b"),
            ("b", "companion", "a"));

        IReadOnlyList<ResolvedRelation> relations = Project(snapshot, "a").Relations;

        await Assert.That(relations.Count(relation => relation.Target.Value == "b")).IsEqualTo(1);
    }

    [Test]
    public async Task A_Self_Relation_Is_Dropped_With_A_Diagnostic()
    {
        Snapshot snapshot = Resolve(("a", "uses", "a"));

        await Assert.That(Project(snapshot, "a").Relations).IsEmpty();
        await Assert.That(snapshot.Diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.RelationsTargetUnknown);
    }

    private static ResolvedProject Project(Snapshot snapshot, string slug) =>
        snapshot.Localizations[snapshot.DefaultLocale].Projects.Single(project => project.Slug.Value == slug);

    private static Snapshot Resolve(params (string From, string Type, string Target)[] relations)
    {
        Portfolio portfolio = Portfolio.Valid();
        HashSet<string> repos = [.. relations.Select(relation => relation.From)];

        foreach (string slug in repos)
        {
            string body = string.Concat(relations
                .Where(relation => relation.From == slug)
                .Select(relation => $"\n[[relations]]\ntype = \"{relation.Type}\"\ntarget = \"{relation.Target}\"\n"));

            _ = portfolio.Project(
                $"dutchy/{slug}",
                new() { [".folio/project.toml"] = $"version = 1\n\n[project]\nslug = \"{slug}\"\n{body}" });
        }

        return portfolio.Resolve().Value;
    }
}
