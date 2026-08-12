using Folio.Domain.Configuration;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class SectionKeysTests
{
    private static readonly IReadOnlyList<SectionEntry> OneOfEach =
    [
        new ProseSectionEntry("prose", "prose.md"),
        new HeroSectionEntry(
            "hero",
            [new HeroActionEntry("work", new Uri("https://example.com"))],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["image"] = "media/a.png" }),
        new SkillsSectionEntry(
            "stack",
            [new SkillCategoryEntry("languages", [new SkillEntry("csharp", SkillLevel.Expert)])]),
        new QaSectionEntry("faq", ["why"]),
        new ContactSectionEntry("reach"),
        new ProjectsSectionEntry("work", true, 3),
    ];

    [Test]
    public async Task Every_Section_Type_Has_A_Case()
    {
        await Assert.That(OneOfEach.Select(entry => entry.Type))
            .IsEquivalentTo(Enum.GetValues<SectionType>());
    }

    [Test]
    public async Task Prose_Is_The_Only_Type_That_Declares_No_Words()
    {
        SectionType[] wordless =
        [
            .. OneOfEach.Where(entry => !SectionKeys.All(entry).Any()).Select(entry => entry.Type),
        ];

        // A new type falling through the switch would land here and fail.
        await Assert.That(wordless).IsEquivalentTo([SectionType.Prose]);
    }

    [Test]
    public async Task Every_Key_Is_Prefixed_By_Its_Section()
    {
        foreach (SectionEntry entry in OneOfEach)
        {
            foreach (string key in SectionKeys.All(entry))
            {
                await Assert.That(key).StartsWith($"section.{entry.Id}.");
            }
        }
    }
}
