using FluentAssertions;
using Auraly.Platform.Application.Agents.Operations.Support;
using Auraly.Platform.Application.Commerce;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ProductionProductResolutionRegressionTests
{
    [Fact]
    public void Resolve_SemanticallyMatchingInactiveIdentity_ReturnsUnavailableInsteadOfNotFound()
    {
        var product = new ProductReference(
            Guid.NewGuid(), "CF04", "CF04", "CHORIZO SALSAN X 20 UND 1K",
            null, null, 0m, "COP", 0m)
        { IsActive = false };

        var result = ProductResolutionEngine.Resolve(
            "chorizo salsan",
            [new RetrievedProductCandidate(product, ProductMatchSource.LocalLexicalIndex)]);

        result.Status.Should().Be(ProductResolutionStatus.Unavailable);
        result.Candidates.Should().ContainSingle(candidate => candidate.Product == product);
    }

    [Fact]
    public void NormalizeSearchReference_RemovesExplicitExclusionClause()
    {
        var result = ProductSelectionMemory.NormalizeSearchReference(
            "ranchera Salsan, aclarando que no fuera la salchicha pequeña");

        result.Should().Be("ranchera salsan");
    }
}
