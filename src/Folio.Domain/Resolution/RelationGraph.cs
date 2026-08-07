using Folio.Domain.Configuration;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Resolution;

/// <summary>Resolves declared relations and generates their inverses.</summary>
internal sealed class RelationGraph
{
    private readonly Dictionary<Slug, List<(RelationType Type, Slug Target, bool Generated)>> _edges = [];

    /// <summary>Builds the graph from every project's declared relations.</summary>
    /// <param name="declarations">Each project's slug and the relations it declares.</param>
    /// <param name="known">Every slug in the portfolio.</param>
    /// <param name="sinkFor">Supplies a sink scoped to a project.</param>
    /// <returns>The graph, with inverses generated.</returns>
    public static RelationGraph Build(
        IReadOnlyList<(Slug Slug, IReadOnlyList<RelationEntry> Relations)> declarations,
        IReadOnlySet<Slug> known,
        Func<Slug, DiagnosticSink> sinkFor)
    {
        RelationGraph graph = new();

        foreach ((Slug slug, IReadOnlyList<RelationEntry> relations) in declarations)
        {
            foreach (RelationEntry relation in relations)
            {
                if (!Slug.TryParse(relation.Target, out Slug target) || !known.Contains(target))
                {
                    sinkFor(slug).Warning(
                        DiagnosticCodes.RelationsTargetUnknown,
                        $"Relation target '{relation.Target}' is not in the portfolio; the relation was dropped.");
                    continue;
                }

                if (target.Equals(slug))
                {
                    sinkFor(slug).Warning(
                        DiagnosticCodes.RelationsTargetUnknown,
                        $"Relation target '{relation.Target}' is the project itself; the relation was dropped.");
                    continue;
                }

                graph.Add(slug, relation.Type, target, generated: false);
                graph.Add(target, RelationVocabulary.Invert(relation.Type), slug, generated: true);
            }
        }

        return graph;
    }

    /// <summary>Gets one project's relations, deterministically ordered.</summary>
    /// <param name="slug">The project to read.</param>
    /// <returns>The edges, declared before generated, then by type and target.</returns>
    public IEnumerable<(RelationType Type, Slug Target, bool Generated)> For(Slug slug) =>
        _edges.TryGetValue(slug, out List<(RelationType, Slug, bool)>? edges)
            ? edges
                // A declared edge and its generated twin (only companion, which is self-inverse) collapse to one.
                .GroupBy(edge => (edge.Item1, edge.Item2))
                .Select(group => group.OrderBy(edge => edge.Item3).First())
                .OrderBy(edge => edge.Item3)
                .ThenBy(edge => edge.Item1)
                .ThenBy(edge => edge.Item2.Value, StringComparer.Ordinal)
            : [];

    private void Add(Slug source, RelationType type, Slug target, bool generated)
    {
        if (!_edges.TryGetValue(source, out List<(RelationType, Slug, bool)>? edges))
        {
            edges = [];
            _edges[source] = edges;
        }

        edges.Add((type, target, generated));
    }
}
