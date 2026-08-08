using Folio.Domain.Diagnostics;
using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class SlugTests
{
    [Test]
    [Arguments("folio")]
    [Arguments("folio-core")]
    [Arguments("a")]
    [Arguments("v2")]
    [Arguments("2fa")]
    [Arguments("a-b-c")]
    public async Task Lowercase_Letters_Digits_And_Hyphens_Are_Accepted(string value)
    {
        bool parsed = Slug.TryParse(value, out Slug slug);

        await Assert.That(parsed).IsTrue();
        await Assert.That(slug.Value).IsEqualTo(value);
    }

    [Test]
    [Arguments("")]
    [Arguments("Folio")]
    [Arguments("folio_core")]
    [Arguments("folio.core")]
    [Arguments("folio core")]
    [Arguments("folio/core")]
    [Arguments("-folio")]
    [Arguments("folio-")]
    [Arguments("café")]
    public async Task Anything_Else_Is_Refused(string value)
    {
        await Assert.That(Slug.TryParse(value, out _)).IsFalse();
    }

    [Test]
    [Arguments("Folio", "folio")]
    [Arguments("ResourcePackIdentifier", "resourcepackidentifier")]
    [Arguments("folio.core", "folio-core")]
    [Arguments("My_Project", "my-project")]
    [Arguments("folio-core", "folio-core")]
    [Arguments("a--b", "a-b")]
    [Arguments("-leading", "leading")]
    [Arguments("trailing-", "trailing")]
    public async Task A_Derived_Slug_Is_Normalized(string directory, string expected)
    {
        bool derived = Slug.TryDerive(directory, out Slug slug);

        await Assert.That(derived).IsTrue();
        await Assert.That(slug.Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("___")]
    [Arguments("...")]
    public async Task A_Directory_With_Nothing_Usable_Derives_No_Slug(string directory)
    {
        await Assert.That(Slug.TryDerive(directory, out _)).IsFalse();
    }

    [Test]
    public async Task A_Repository_Name_With_Capitals_Still_Yields_A_Project()
    {
        // Repository names permit capitals, so refusing them would make most repos unlistable.
        Portfolio portfolio = Portfolio.Valid().Project("dutchy/MyProject");

        Loom.Results.Result<Snapshot> result = portfolio.Resolve();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Localizations[result.Value.DefaultLocale].Projects.Select(p => p.Slug.Value))
            .IsEquivalentTo(["myproject"]);
    }

    [Test]
    public async Task A_Slug_Collision_Drops_The_Later_Project_Not_The_Build()
    {
        Loom.Results.Result<Snapshot> result = Portfolio.Valid()
            .Project("dutchy/first", new() { [".folio/project.toml"] = "[project]\nslug = \"same\"\n" })
            .Project("dutchy/second", new() { [".folio/project.toml"] = "[project]\nslug = \"same\"\n" })
            .Resolve();

        await Assert.That(result.IsSuccess).IsTrue();

        IReadOnlyList<ResolvedProject> projects = result.Value.Localizations[result.Value.DefaultLocale].Projects;

        await Assert.That(projects.Select(p => p.Repo)).IsEquivalentTo(["dutchy/first"]);
        await Assert.That(result.Value.Diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.CentralDuplicateSlug);
    }

    [Test]
    public async Task An_Authored_Slug_Is_Never_Repaired()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new() { [".folio/project.toml"] = "[project]\nslug = \"My_Project\"\n" })
            .Diagnostics();

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.ProjectSlugInvalid);
        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("not a valid slug")))
            .IsTrue();
    }
}
