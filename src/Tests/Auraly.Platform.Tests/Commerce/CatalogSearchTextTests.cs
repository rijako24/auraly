using FluentAssertions;
using Auraly.Platform.Domain.Catalog;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public class CatalogSearchTextTests
{
    [Fact]
    public void ContainsAllTerms_matches_product_name_when_query_omits_connector_words()
    {
        var matches = CatalogSearchText.ContainsAllTerms(
            "vino mango",
            "Vino de Mango 750 ml",
            "Refrescante y tropical");

        matches.Should().BeTrue();
    }

    [Fact]
    public void ContainsAllTerms_ignores_accents_and_spanish_connector_words()
    {
        var matches = CatalogSearchText.ContainsAllTerms(
            "promocion dia de la madre",
            "Promoci\u00f3n D\u00eda de las Madres");

        matches.Should().BeTrue();
    }

    [Theory]
    [InlineData("pechugas", "pechuga")]
    [InlineData("perniles", "pernil")]
    [InlineData("productos", "producto")]
    public void GetFallbackQueries_includes_common_singular_forms(string query, string expected)
    {
        var fallbacks = CatalogSearchText.GetFallbackQueries(query);

        fallbacks.Should().Contain(expected);
        fallbacks.Should().NotContain(query);
    }

    [Fact]
    public void GetFallbackQueries_prefers_full_query_before_individual_terms()
    {
        var fallbacks = CatalogSearchText.GetFallbackQueries("pechugas de pollo");

        fallbacks.Should().NotBeEmpty();
        fallbacks[0].Should().Be("pechuga pollo");
        fallbacks.Should().Contain("pechugas");
        fallbacks.Should().Contain("pollo");
    }
}
