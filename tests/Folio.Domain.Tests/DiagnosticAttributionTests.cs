using Folio.Domain.Diagnostics;

namespace Folio.Domain.Tests;

public sealed class DiagnosticAttributionTests
{
    [Test]
    public async Task A_Slug_Collision_Keeps_The_Surviving_Projects_Own_Diagnostics()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "[project]\nslug = \"same\"\nstatus = \"bogus\"\n",
            })
            .Project("dutchy/b", new()
            {
                [".folio/project.toml"] = "[project]\nslug = \"same\"\nrole = \"bogus\"\n",
            })
            .Diagnostics();

        // The winner declares an unknown status, the loser an unknown role; both must survive.
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Message))
            .Contains(message => message.Contains("status", StringComparison.Ordinal));
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Message))
            .Contains(message => message.Contains("role", StringComparison.Ordinal));
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.CentralDuplicateSlug);
    }

    [Test]
    public async Task A_Dropped_Project_Keeps_Its_Project_Stamp()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/a", new()
            {
                [".folio/project.toml"] = "version = 1\n\n[project]\nslug = \"a\"\n",
                [".folio/content/de/x.md"] = "# X\n",
            })
            .Diagnostics();

        Diagnostic dropped = diagnostics.First(
            diagnostic => diagnostic.Code == DiagnosticCodes.LocaleContentDirUndeclared);

        await Assert.That(dropped.Project).IsEqualTo("a");
    }

    [Test]
    public async Task A_Repository_Name_Is_Filed_Under_Its_Derived_Slug()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Central(".folio/projects.toml", "version = 1\n\n[[projects]]\nrepo = \"dutchy/Folio.Api\"\n")
            .Diagnostics();

        Diagnostic missing = diagnostics.First(
            diagnostic => diagnostic.Code == DiagnosticCodes.ProjectNotFound);

        await Assert.That(missing.Project).IsEqualTo("folio-api");
    }

    [Test]
    public async Task An_Unparseable_Project_Is_Filed_Under_Its_Derived_Slug()
    {
        IReadOnlyList<Diagnostic> diagnostics = Portfolio.Valid()
            .Project("dutchy/Folio.Api", new() { [".folio/project.toml"] = "[project\nslug = " })
            .Diagnostics();

        Diagnostic unparseable = diagnostics.First(
            diagnostic => diagnostic.Code == DiagnosticCodes.ProjectUnparseable);

        await Assert.That(unparseable.Project).IsEqualTo("folio-api");
    }
}
