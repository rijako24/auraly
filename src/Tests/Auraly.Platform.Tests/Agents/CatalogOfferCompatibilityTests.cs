using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Models;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class CatalogOfferSchemaTests
{
    [Fact]
    public async Task CurrentSnapshotPreservesIdentityPriceAndStockDuringResolution()
    {
        var productId = Guid.NewGuid();
        var context = new AgentConversationContext
        {
            ConversationState = new ConversationState(),
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>
            {
                ["system.catalog_products"] = $$"""
                    {
                      "schemaVersion":2,
                      "sequence":1,
                      "snapshots":[{
                        "sequence":1,
                        "searchTerms":["salchicha"],
                        "products":[{
                          "productId":"{{productId}}",
                          "externalProductId":"CF59",
                          "sku":"CF59",
                          "name":"SALCHICHA LONG X 550GR",
                          "unitPrice":16023.21,
                          "currency":"COP",
                          "stockQuantity":49
                        }]
                      }]
                    }
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
