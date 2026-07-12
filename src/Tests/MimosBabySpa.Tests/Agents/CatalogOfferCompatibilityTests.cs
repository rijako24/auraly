using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CatalogOfferCompatibilityTests
{
    [Fact]
    public async Task LegacySnapshotPreservesIdentityPriceAndStockDuringResolution()
    {
        var productId = Guid.NewGuid();
        var context = new AgentConversationContext
        {
            ConversationState = new ConversationState(),
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>
            {
                ["system.catalog_products"] = $$"""
                    [{
                      "product_id":"{{productId}}",
                      "external_product_id":"CF59",
                      "sku":"CF59",
                      "name":"SALCHICHA LONG X 550GR",
                      "unit_price":16023.21,
                      "currency":"COP",
                      "stock_quantity":49
                    }]
                    """
            }
        };
        var commerce = new Mock<ICommerceService>();
        var resolver = new CommerceCartProductResolver(commerce.Object);

        var matches = await resolver.FindAsync(context, "salchicha long x 550 gr");

        matches.Should().ContainSingle();
        matches[0].ProductId.Should().Be(productId);
        matches[0].ExternalProductId.Should().Be("CF59");
        matches[0].Sku.Should().Be("CF59");
        matches[0].UnitPrice.Should().Be(16023.21m);
        matches[0].StockQuantity.Should().Be(49m);
        commerce.Verify(service => service.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductSearchRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
