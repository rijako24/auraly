using FluentAssertions;
using MimosBabySpa.Domain.Catalog;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

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
}
