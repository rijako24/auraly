using FluentAssertions;
using MimosBabySpa.Domain.Catalog;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class CatalogPrefixFallbackRegressionTests
{
    [Fact]
    public void GetFallbackQueries_IncludesControlledPrefixForLongSingleTerm()
    {
        var result = CatalogSearchText.GetFallbackQueries("champiñón");

        result.Should().Contain("champi");
    }
}
