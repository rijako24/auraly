using FluentAssertions;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductResolutionEngineTests
{
    [Fact]
    public void Resolve_ExactSku_AutoResolves()
    {
        var product = Product("JAMON CUNIT X 500GR", "CF17");
        var result = Resolve("CF17", product);
        result.Status.Should().Be(ProductResolutionStatus.Resolved);
        result.Selected.Should().Be(product);
    }

    [Fact]
    public void Resolve_KnownCustomerAlias_AutoResolvesByIdentity()
    {
        var product = Product("JAMON CUNIT X 500GR", "CF17");
        var result = ProductResolutionEngine.Resolve("jamonada cunichef",
            [new(product, ProductMatchSource.CustomerAlias, ExactAlias: true, CanAutoResolve: true)]);
        result.Status.Should().Be(ProductResolutionStatus.Resolved);
        result.Selected.Should().Be(product);
    }

    [Fact]
    public void Resolve_SuggestOnlyAlias_NeverAutoAdds()
    {
        var product = Product("JAMON CUNIT X 500GR", "CF17");
        var result = ProductResolutionEngine.Resolve("jamonada cunichef",
            [new(product, ProductMatchSource.BusinessAlias, ExactAlias: true, CanAutoResolve: false)]);
        result.Status.Should().Be(ProductResolutionStatus.SuggestionRequired);
        result.Selected.Should().BeNull();
        result.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_CjMisspelling_ReturnsCredibleSuggestionWithoutAutoAdding()
    {
        var target = Product("JAMON CUNIT X 500GR", "CF17");
        var noise = Product("JAMON SANDWICH PIETRAN X 500GR", "PI12");
        var result = Resolve("jamonada cunichef", target, noise);
        result.Status.Should().Be(ProductResolutionStatus.SuggestionRequired);
        result.Selected.Should().BeNull();
        result.Candidates.Should().ContainSingle(candidate => candidate.Product == target);
    }

    [Fact]
    public void Resolve_PresentationNumber_IsAHardConstraint()
    {
        var small = Product("SALCHICHA LONG X 550GR", "CF59");
        var large = Product("SALCHICHA LONG X 1100GR", "CF20");
        var result = Resolve("salchicha long 550", small, large);
        result.Status.Should().Be(ProductResolutionStatus.Resolved);
        result.Selected.Should().Be(small);
    }

    [Fact]
    public void Resolve_GenericFamilyWithTwoPresentations_RemainsAmbiguous()
    {
        var result = Resolve("salchicha long",
            Product("SALCHICHA LONG X 550GR", "CF59"),
            Product("SALCHICHA LONG X 1100GR", "CF20"));
        result.Status.Should().Be(ProductResolutionStatus.Ambiguous);
        result.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_UnrelatedText_ReturnsNotFoundWithNoInventedOptions()
    {
        var result = Resolve("producto marciano azul", Product("JAMON CUNIT X 500GR", "CF17"));
        result.Status.Should().Be(ProductResolutionStatus.NotFound);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ExactAliasForInactiveProduct_ReturnsUnavailable()
    {
        var inactive = Product("JAMON CUNIT X 500GR", "CF17") with { IsActive = false };
        var result = ProductResolutionEngine.Resolve("jamonada cunichef",
            [new(inactive, ProductMatchSource.CustomerAlias, ExactAlias: true, CanAutoResolve: true)]);

        result.Status.Should().Be(ProductResolutionStatus.Unavailable);
        result.Selected.Should().BeNull();
        result.Candidates.Should().ContainSingle(candidate => candidate.Product == inactive);
    }

    [Fact]
    public void Resolve_ExactNativeIdentityForInactiveProduct_ReturnsUnavailable()
    {
        var inactive = Product("JAMON CUNIT X 500GR", "CF17") with { IsActive = false };
        var result = Resolve("CF17", inactive);

        result.Status.Should().Be(ProductResolutionStatus.Unavailable);
        result.Selected.Should().BeNull();
    }

    [Fact]
    public void Resolve_PackagingWordMissingFromSupplierName_ReturnsSuggestionNotSilentSelection()
    {
        var product = Product("CHORIZO SALSAN X 20 UND 1K", "CF04");

        var result = Resolve("paquetes de chorizo Salsan", product);

        result.Status.Should().Be(ProductResolutionStatus.SuggestionRequired);
        result.Candidates.Should().ContainSingle(candidate => candidate.Product == product);
        result.Selected.Should().BeNull();
    }

    [Fact]
    public void Resolve_PackagingWordDoesNotHideRealFamilyAmbiguity()
    {
        var result = Resolve("caja de papas",
            Product("PAPA FARM FRITES X 2.5K", "CG29"),
            Product("PAPA GOLDEN PREMIUM 2.5KG", "CG28"));

        result.Status.Should().Be(ProductResolutionStatus.Ambiguous);
        result.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_PackagingWordStillReportsUnavailableExactBrand()
    {
        var product = Product("CHORIZO SALSAN X 20 UND 1K", "CF04") with { IsActive = false };
        Resolve("paquetes de chorizo Salsan", product).Status.Should().Be(ProductResolutionStatus.Unavailable);
    }

    [Fact]
    public void Resolve_ShortBrandSpellingVariation_ReturnsSuggestion()
    {
        var result = Resolve("leche de coco Kary", Product("LECHE DE COCO KARIX 400", "PV32"));

        result.Status.Should().Be(ProductResolutionStatus.SuggestionRequired);
        result.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void SearchKeys_IncludeMorphologicalStemAndStablePrefix()
    {
        ProductSearchText.GetSearchKeys("jamonada cunichef")
            .Should().Contain(["jamon", "cuni"]);
        ProductSearchText.GetIndexTerms("JAMON CUNIT X 500GR")
            .Should().Contain(["jamon", "cuni", "500", "gr"]);
    }

    [Fact]
    public void SearchKeys_UseNGramsAndPhoneticsForMisspelledNames()
    {
        var requested = ProductSearchText.GetSearchKeys("cunichef");
        var indexed = ProductSearchText.GetIndexTerms("CUNNY CHEF");

        requested.Intersect(indexed).Should().Contain(term => term.StartsWith("g:", StringComparison.Ordinal));
        ProductSearchText.GetSearchKeys("kesito")
            .Intersect(ProductSearchText.GetIndexTerms("QUESITO"))
            .Should().Contain(term => term.StartsWith("p:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("500g", "500", "g")]
    [InlineData("1L", "1", "l")]
    [InlineData("250 ml", "250", "ml")]
    public void SearchTokens_PreservePresentationUnits(string text, string amount, string unit)
    {
        ProductSearchText.GetTokens(text).Should().Contain([amount, unit]);
    }

    private static ProductResolution Resolve(string text, params ProductReference[] products) =>
        ProductResolutionEngine.Resolve(text,
            products.Select(product => new RetrievedProductCandidate(product, ProductMatchSource.LocalLexicalIndex)).ToList());

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
