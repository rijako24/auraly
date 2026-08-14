using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Operations.Support;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class CatalogOfferDeduplicationTests
{
    [Fact]
    public async Task RepeatedProductIsKeptOnceWithTheMostRecentAuthoritativeData()
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Config = new AgentConfig
            {
                Commerce = new CommerceConfig
                {
                    Enabled = true,
                    OfferMemoryMaxSnapshots = 8,
                    OfferMemoryMaxProducts = 100
                }
            }
        };
        var facts = new Mock<IConversationFactsService>();

        await CatalogOfferMemory.RememberAsync(
            facts.Object,
            context,
            [Product("CF59", 16023.21m, 49m)],
            ["salchicha"],
            CancellationToken.None);
        await CatalogOfferMemory.RememberAsync(
            facts.Object,
            context,
            [Product("CF59", 16500m, 40m)],
            ["salchicha long"],
            CancellationToken.None);

        var memory = CatalogOfferMemory.Read(context.Facts);
        memory.Should().NotBeNull();
        var products = CatalogOfferMemory.AllProducts(memory!);
        products.Should().ContainSingle();
        products[0].UnitPrice.Should().Be(16500m);
        products[0].StockQuantity.Should().Be(40m);
        memory!.Snapshots.Sum(snapshot => snapshot.Products.Count).Should().Be(1);
        memory.Snapshots.Should().ContainSingle().Which.SearchTerms.Should().Equal("salchicha long");
    }

    private static ProductReference Product(string externalId, decimal price, decimal stock) =>
        new(null, externalId, externalId, "SALCHICHA LONG X 550GR", null, null, price, "COP", stock);
}
