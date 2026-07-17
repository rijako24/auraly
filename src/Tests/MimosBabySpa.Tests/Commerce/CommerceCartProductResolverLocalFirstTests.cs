using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class CommerceCartProductResolverLocalFirstTests
{
    [Fact]
    public async Task ResolveAsync_ResolvesLocallyThenQuotesExactIdentity()
    {
        var local = Product("CF17", "JAMON CUNIT X 500GR", 0m);
        var live = local with { UnitPrice = 18_900m, StockQuantity = 12m };
        var commerce = new Mock<ICommerceService>();
        commerce.As<IProductLookupService>()
            .Setup(service => service.GetProductAsync(
                It.IsAny<AgentConversationContext>(),
                It.Is<ProductLookupRequest>(request =>
                    request.ExternalProductId == "CF17" && request.Sku == "CF17"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);
        var candidates = new Mock<IProductCandidateRetriever>();
        candidates.Setup(service => service.RetrieveAsync(
                It.IsAny<AgentConversationContext>(),
                "CF17",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievedProductCandidate(local, ProductMatchSource.LocalLexicalIndex)]);
        var resolver = new CommerceCartProductResolver(commerce.Object, candidates.Object);

        var result = await resolver.ResolveAsync(Context(), "CF17");

        result.Status.Should().Be(ProductResolutionStatus.Resolved);
        result.Selected!.UnitPrice.Should().Be(18_900m);
        commerce.Verify(service => service.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductSearchRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_WhenPresentationsAreAmbiguous_QuotesEveryFinalCandidate()
    {
        var commerce = new Mock<ICommerceService>();
        var lookup = commerce.As<IProductLookupService>();
        var small = Product("CF59", "SALCHICHA LONG X 550GR", 0m);
        var large = Product("CF20", "SALCHICHA LONG X 1100GR", 0m);
        lookup.Setup(service => service.GetProductAsync(
                It.IsAny<AgentConversationContext>(),
                It.IsAny<ProductLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductLookupRequest request, CancellationToken _) =>
                request.ExternalProductId == "CF59"
                    ? small with { UnitPrice = 10_000m, StockQuantity = 8m }
                    : large with { UnitPrice = 18_000m, StockQuantity = 4m });
        var candidates = new Mock<IProductCandidateRetriever>();
        candidates.Setup(service => service.RetrieveAsync(
                It.IsAny<AgentConversationContext>(),
                "salchicha long",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RetrievedProductCandidate(small, ProductMatchSource.LocalLexicalIndex),
                new RetrievedProductCandidate(large, ProductMatchSource.LocalLexicalIndex)
            ]);
        var resolver = new CommerceCartProductResolver(commerce.Object, candidates.Object);

        var result = await resolver.ResolveAsync(Context(), "salchicha long");

        result.Status.Should().Be(ProductResolutionStatus.Ambiguous);
        result.Candidates.Select(candidate => candidate.Product.UnitPrice)
            .Should().BeEquivalentTo([10_000m, 18_000m]);
        lookup.Verify(service => service.GetProductAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductLookupRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveAsync_WhenSuggestedProductHasNoWarehouseStock_ReturnsUnavailable()
    {
        var local = Product("CF04", "CHORIZO SALSAN X 20 UND 1K", 0m);
        var commerce = new Mock<ICommerceService>();
        commerce.As<IProductLookupService>()
            .Setup(service => service.GetProductAsync(
                It.IsAny<AgentConversationContext>(),
                It.IsAny<ProductLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductReference?)null);
        var candidates = new Mock<IProductCandidateRetriever>();
        candidates.Setup(service => service.RetrieveAsync(
                It.IsAny<AgentConversationContext>(),
                "paquetes de chorizo Salsan",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievedProductCandidate(local, ProductMatchSource.LocalLexicalIndex)]);
        var resolver = new CommerceCartProductResolver(commerce.Object, candidates.Object);

        var result = await resolver.ResolveAsync(Context(), "paquetes de chorizo Salsan");

        result.Status.Should().Be(ProductResolutionStatus.Unavailable);
        result.Candidates.Should().ContainSingle(candidate => candidate.Product.Name == local.Name);
    }

    private static AgentConversationContext Context() =>
        new() { BusinessId = Guid.NewGuid(), ConversationId = Guid.NewGuid() };

    private static ProductReference Product(string code, string name, decimal price) =>
        new(Guid.NewGuid(), code, code, name, null, null, price, "COP", null);
}
