using Folio.Domain.Model;

namespace Folio.Domain.Tests;

public sealed class LanguageShareTests
{
    [Test]
    public async Task Shares_Are_Rounded_To_One_Decimal_Place()
    {
        IReadOnlyList<LanguageShare> shares = Metadata(
            new RepoLanguage("Rust", 900),
            new RepoLanguage("Shell", 100),
            new RepoLanguage("Markdown", 33)).LanguageShares;

        await Assert.That(shares.Select(share => share.Percent)).IsEquivalentTo([87.1, 9.7, 3.2]);
    }

    [Test]
    public async Task Shares_Keep_The_Order_Of_The_Breakdown_They_Came_From()
    {
        IReadOnlyList<LanguageShare> shares = Metadata(
            new RepoLanguage("Rust", 900),
            new RepoLanguage("Shell", 100)).LanguageShares;

        await Assert.That(shares.Select(share => share.Name))
            .IsEquivalentTo(["Rust", "Shell"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Repository_With_No_Measured_Bytes_Divides_By_Nothing()
    {
        IReadOnlyList<LanguageShare> shares = Metadata(new RepoLanguage("Rust", 0)).LanguageShares;

        await Assert.That(shares[0].Percent).IsEqualTo(0);
    }

    [Test]
    public async Task No_Languages_Produce_No_Shares()
    {
        await Assert.That(Metadata().LanguageShares).IsEmpty();
    }

    private static RepoMetadata Metadata(params RepoLanguage[] languages) =>
        Fixture.Metadata("dutchy/folio") with { Languages = languages };
}
