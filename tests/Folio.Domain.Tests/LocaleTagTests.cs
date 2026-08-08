using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class LocaleTagTests
{
    [Test]
    [Arguments("en", "en")]
    [Arguments("EN", "en")]
    [Arguments("nl-be", "nl-BE")]
    [Arguments("NL-BE", "nl-BE")]
    [Arguments("pt-br", "pt-BR")]
    [Arguments("zh-hant-tw", "zh-Hant-TW")]
    public async Task Parsing_Canonicalizes_Subtags(string input, string expected)
    {
        bool parsed = LocaleTag.TryParse(input, out LocaleTag tag);

        await Assert.That(parsed).IsTrue();
        await Assert.That(tag.Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("e")]
    [Arguments("en-")]
    [Arguments("1234")]
    public async Task Parsing_Refuses_A_Malformed_Tag(string input)
    {
        bool parsed = LocaleTag.TryParse(input, out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task Parsing_Is_Case_Insensitive_So_Two_Spellings_Are_One_Locale()
    {
        _ = LocaleTag.TryParse("nl-be", out LocaleTag lower);
        _ = LocaleTag.TryParse("NL-BE", out LocaleTag upper);

        await Assert.That(lower).IsEqualTo(upper);
    }

    [Test]
    public async Task Truncation_Strips_The_Rightmost_Subtag()
    {
        _ = LocaleTag.TryParse("zh-Hant-TW", out LocaleTag tag);

        bool first = tag.TryTruncate(out LocaleTag script);
        bool second = script.TryTruncate(out LocaleTag language);

        await Assert.That(first).IsTrue();
        await Assert.That(script.Value).IsEqualTo("zh-Hant");
        await Assert.That(second).IsTrue();
        await Assert.That(language.Value).IsEqualTo("zh");
    }

    [Test]
    public async Task Truncation_Stops_At_The_Language()
    {
        _ = LocaleTag.TryParse("en", out LocaleTag tag);

        bool truncated = tag.TryTruncate(out _);

        await Assert.That(truncated).IsFalse();
    }
}
