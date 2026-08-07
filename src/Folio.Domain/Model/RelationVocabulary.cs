using System.Collections.Frozen;

namespace Folio.Domain.Model;

/// <summary>The relation types, each with its TOML name and the type generated on the target.</summary>
public static class RelationVocabulary
{
    // The one place a relation type is defined. Adding a pair here is the whole change.
    private static readonly (RelationType Type, string Name, RelationType Inverse, string InverseName)[] Pairs =
    [
        (RelationType.Uses, "uses", RelationType.UsedBy, "used-by"),
        (RelationType.PartOf, "part-of", RelationType.Contains, "contains"),
        (RelationType.SuccessorOf, "successor-of", RelationType.PredecessorOf, "predecessor-of"),
        (RelationType.Companion, "companion", RelationType.Companion, "companion"),
    ];

    private static readonly FrozenDictionary<RelationType, string> NamesByType = Build();

    private static readonly FrozenDictionary<RelationType, RelationType> InversesByType =
        Pairs.SelectMany(pair => new[] { (pair.Type, pair.Inverse), (pair.Inverse, pair.Type) })
            .DistinctBy(entry => entry.Item1)
            .ToFrozenDictionary(entry => entry.Item1, entry => entry.Item2);

    /// <summary>Gets the types a project may declare, by their TOML name.</summary>
    public static FrozenDictionary<string, RelationType> Declarable { get; } =
        Pairs.ToFrozenDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal);

    /// <summary>Gets every relation type, declarable and generated alike.</summary>
    public static IReadOnlyCollection<RelationType> All => NamesByType.Keys;

    /// <summary>Gets the TOML name of a relation type, including the generated inverses.</summary>
    /// <param name="type">The relation type.</param>
    /// <returns>The hyphenated name used as a locale key.</returns>
    public static string Name(RelationType type) =>
        NamesByType.TryGetValue(type, out string? name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(type));

    /// <summary>Gets the type generated on the target of a declared relation.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns>The inverse type.</returns>
    public static RelationType Invert(RelationType type) =>
        InversesByType.TryGetValue(type, out RelationType inverse)
            ? inverse
            : throw new ArgumentOutOfRangeException(nameof(type));

    private static FrozenDictionary<RelationType, string> Build() =>
        Pairs.SelectMany(pair => new[] { (pair.Type, pair.Name), (pair.Inverse, pair.InverseName) })
            .DistinctBy(entry => entry.Item1)
            .ToFrozenDictionary(entry => entry.Item1, entry => entry.Item2);
}
