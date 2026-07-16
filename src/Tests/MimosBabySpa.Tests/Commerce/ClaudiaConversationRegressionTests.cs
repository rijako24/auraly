using FluentAssertions;
using MimosBabySpa.Application.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ClaudiaConversationRegressionTests
{
    [Fact]
    public void CunichefReference_ReturnsConcreteSuggestionInsteadOfAnEmptyAmbiguity()
    {
        var target = Product("JAMON CUNIT X 500GR", "CF17");
        var alternative = Product("JAMON SANDWICH PIETRAN X 500GR", "PI12");

        var result = ProductResolutionEngine.Resolve(
            "jamonada cunichef",
            [
                new(target, ProductMatchSource.LocalLexicalIndex),
                new(alternative, ProductMatchSource.LocalLexicalIndex)
            ]);

        result.Status.Should().Be(ProductResolutionStatus.SuggestionRequired);
        result.Candidates.Should().NotBeEmpty();
        result.Candidates.Should().Contain(candidate => candidate.Product.Name == target.Name);
    }

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
