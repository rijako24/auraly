using FluentAssertions;
using Auraly.Platform.Domain.Catalog;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class CatalogPrefixFallbackRegressionTests
{
    [Fact]
    public void GetFallbackQueries_IncludesControlledPrefixForLongSingleTerm()
    {
        var result = CatalogSearchText.GetFallbackQueries("champiñón");

        result.Should().Contain("champi");
    }
}
