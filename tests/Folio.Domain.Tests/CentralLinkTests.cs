using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class CentralLinkTests
{
    [Test]
    [Arguments("github", "javascript:alert(1)")]
    [Arguments("github", "mailto:someone@example.com")]
    [Arguments("website", "file:///etc/passwd")]
    public async Task A_Non_Email_Link_Accepts_Only_Http(string type, string url)
    {
        await Assert.That(Codes(type, url)).Contains(DiagnosticCodes.SchemaInvalidValue);
    }

    [Test]
    public async Task An_Email_Link_Accepts_Mailto()
    {
        await Assert.That(Codes("email", "mailto:someone@example.com"))
            .DoesNotContain(DiagnosticCodes.SchemaInvalidValue);
    }

    [Test]
    public async Task An_Absent_Default_Locale_Is_Unparseable_Rather_Than_Undeclared()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Central(".folio/site.toml", """
                version = 1

                [site]
                url            = "https://dutchy.dev"
                default_locale = "not a tag"
                locales        = ["en"]
                owner          = "dutchy"
                """)
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.CentralUnparseable);
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .DoesNotContain(DiagnosticCodes.CentralDefaultLocaleUndeclared);
    }

    private static IEnumerable<string> Codes(string type, string url) => Portfolio.Valid()
        .Central(".folio/site.toml", $"""
            version = 1

            [site]
            url            = "https://dutchy.dev"
            default_locale = "en"
            locales        = ["en"]
            owner          = "dutchy"

            [[site.links]]
            type = "{type}"
            url  = "{url}"
            """)
        .Diagnostics()
        .Select(diagnostic => diagnostic.Code);
}
