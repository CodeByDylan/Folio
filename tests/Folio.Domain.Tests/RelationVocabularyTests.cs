using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class RelationVocabularyTests
{
    [Test]
    public async Task Every_Relation_Type_Has_A_Name_And_An_Inverse()
    {
        foreach (RelationType type in Enum.GetValues<RelationType>())
        {
            await Assert.That(RelationVocabulary.Name(type)).IsNotEmpty();
            await Assert.That(() => RelationVocabulary.Invert(type)).ThrowsNothing();
        }
    }

    [Test]
    public async Task Inverting_Twice_Returns_The_Original()
    {
        foreach (RelationType type in Enum.GetValues<RelationType>())
        {
            await Assert.That(RelationVocabulary.Invert(RelationVocabulary.Invert(type))).IsEqualTo(type);
        }
    }

    [Test]
    public async Task Names_Are_Unique()
    {
        IEnumerable<string> names = Enum.GetValues<RelationType>().Select(RelationVocabulary.Name);

        await Assert.That(names.Distinct()).Count().IsEqualTo(Enum.GetValues<RelationType>().Length);
    }

    [Test]
    public async Task Only_The_Non_Generated_Half_Can_Be_Declared()
    {
        await Assert.That(RelationVocabulary.Declarable.Keys)
            .IsEquivalentTo(["uses", "part-of", "successor-of", "companion"]);
    }

    [Test]
    public async Task A_Declarable_Name_Maps_To_The_Type_It_Names()
    {
        foreach ((string name, RelationType type) in RelationVocabulary.Declarable)
        {
            await Assert.That(RelationVocabulary.Name(type)).IsEqualTo(name);
        }
    }
}
