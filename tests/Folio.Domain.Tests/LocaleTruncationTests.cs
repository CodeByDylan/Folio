using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class LocaleTruncationTests
{
    [Test]
    public async Task A_Default_Locale_That_Is_Also_A_Truncation_Reports_Truncation()
    {
        IReadOnlyList<Diagnostic> diagnostics = Resolve(defaultLocale: "nl", locales: "\"nl\", \"nl-BE\"");

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LocaleTruncated);
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.LocaleKeyMissing);
    }

    [Test]
    public async Task An_Unrelated_Default_Locale_Reports_A_Missing_Key()
    {
        IReadOnlyList<Diagnostic> diagnostics = Resolve(defaultLocale: "en", locales: "\"en\", \"nl-BE\"");

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LocaleKeyMissing);
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.LocaleTruncated);
    }

    private static IReadOnlyList<Diagnostic> Resolve(string defaultLocale, string locales) =>
        Portfolio.Valid(locales, defaultLocale)
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project]\nslug = \"a\"\n",
                [$".folio/locales/{defaultLocale}.toml"] = "project.name = \"A\"\n",
            })
            .Diagnostics();
}
