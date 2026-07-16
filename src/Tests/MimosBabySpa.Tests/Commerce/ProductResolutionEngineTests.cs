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
    public void SearchKeys_IncludeMorphologicalStemAndStablePrefix()
    {
        ProductSearchText.GetSearchKeys("jamonada cunichef")
            .Should().Contain(["jamon", "cuni"]);
        ProductSearchText.GetIndexTerms("JAMON CUNIT X 500GR")
            .Should().Contain(["jamon", "cuni", "500", "gr"]);
    }

    private static ProductResolution Resolve(string text, params ProductReference[] products) =>
        ProductResolutionEngine.Resolve(text,
            products.Select(product => new RetrievedProductCandidate(product, ProductMatchSource.LocalLexicalIndex)).ToList());

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
