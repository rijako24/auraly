using FluentAssertions;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductSemanticCoverageRegressionTests
{
    [Fact]
    public void Resolve_MatchingOnlyOneWord_DoesNotInventCandidate()
    {
        var result = Resolve("super ranchera", Product("PAN PERRO SUPER X10", "PA10"));

        result.Status.Should().Be(ProductResolutionStatus.NotFound);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_MultipleMeaningfulTerms_StillFindsLegitimateProduct()
    {
        var expected = Product("SALCHICHA RANCHERA SUPER X 525 GR", "CF22");

        var result = Resolve("super ranchera", expected);

        result.Status.Should().Be(ProductResolutionStatus.Resolved);
        result.Selected.Should().Be(expected);
    }

    private static ProductResolution Resolve(string text, params ProductReference[] products) =>
        ProductResolutionEngine.Resolve(text,
            products.Select(product => new RetrievedProductCandidate(
                product, ProductMatchSource.LocalLexicalIndex)).ToList());

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
