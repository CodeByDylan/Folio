using Folio.Domain.Content;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class InputHardeningTests
{
    [Test]
    [Arguments("%2e%2e/%2e%2e/%2e%2e/%2e%2e/other/x.png")]
    [Arguments("a%2fb/x.png")]
    public async Task A_Percent_Encoded_Traversal_Out_Of_The_Repo_Is_Refused(string target)
    {
        await Assert.That(RepoPath.Resolve(target, ".folio/content/en/s.md")).IsNull();
    }

    [Test]
    public async Task Percent_Encoded_Dot_Dot_Resolves_The_Same_As_Plain_Dot_Dot()
    {
        // The resolved path must be clean, so RawContentUrl cannot collapse a survivor into an escape.
        await Assert.That(RepoPath.Resolve("%2e%2e/%2e%2e/media/hero.png", ".folio/content/en/s.md"))
            .IsEqualTo(".folio/media/hero.png");
    }

    [Test]
    public async Task A_Plain_Relative_Path_Still_Resolves()
    {
        await Assert.That(RepoPath.Resolve("../../media/hero.png", ".folio/content/en/s.md"))
            .IsEqualTo(".folio/media/hero.png");
    }

    [Test]
    [Arguments("en-a\"b")]
    [Arguments("en-a,b")]
    [Arguments("en-a:b")]
    [Arguments("en-a b")]
    public async Task A_Locale_With_Non_Alphanumeric_Subtags_Is_Refused(string value)
    {
        await Assert.That(LocaleTag.TryParse(value, out _)).IsFalse();
    }

    [Test]
    [Arguments("nl-BE")]
    [Arguments("zh-Hant-TW")]
    [Arguments("en")]
    public async Task A_Well_Formed_Locale_Still_Parses(string value)
    {
        await Assert.That(LocaleTag.TryParse(value, out _)).IsTrue();
    }
}
