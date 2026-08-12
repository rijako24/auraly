using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CrossOfferCartApplicationTests
{
    [Fact]
    public async Task AppliesOneAtomicBatchUsingProductsFromDifferentOfferSnapshots()
    {
        var context = new AgentConversationContext
        {
            ConversationState = new ConversationState(),
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>
            {
                ["system.catalog_products"] = """
                    {
                      "schemaVersion":2,
                      "sequence":2,
                      "snapshots":[
                        {
                          "sequence":1,
                          "searchTerms":["pechuga"],
                          "products":[{"externalProductId":"PO63","sku":"PO63","name":"PECHUGA CRIOLLA","unitPrice":14033.67,"currency":"COP","stockQuantity":20}]
                        },
                        {
                          "sequence":2,
                          "searchTerms":["cerdo"],
                          "products":[{"externalProductId":"CE02","sku":"CE02","name":"PIERNA DE CERDO CON PIEL Y HUESO","unitPrice":10319.16,"currency":"COP","stockQuantity":15}]
                        }
                      ]
                    }
                    """
            }
        };
        var commerce = new Mock<ICommerceService>();
        var store = new RecordingStore();
        var processor = new CartCommandBatchProcessor(
            new CommerceCartProductResolver(commerce.Object),
            store);

        var result = await processor.ApplyAsync(context,
        [
            new CartCommand(CartCommandOperations.Add, "pechuga criolla", 2, null),
            new CartCommand(CartCommandOperations.Add, "pierna con piel", 1, null)
        ]);

        result.Success.Should().BeTrue();
        result.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("PECHUGA CRIOLLA", 2m),
            ("PIERNA DE CERDO CON PIEL Y HUESO", 1m));
        commerce.Verify(service => service.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductSearchRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class RecordingStore : ICartMutationStore
    {
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<ResolvedCartCommand> Applied { get; private set; } = [];

        public Task<OrderSnapshot> GetCurrentAsync(
            AgentConversationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot());

        public Task<OrderSnapshot> ApplyAtomicallyAsync(
            AgentConversationContext context,
            IReadOnlyList<ResolvedCartCommand> commands,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            Applied = commands;
            return Task.FromResult(Snapshot());
        }

        private static OrderSnapshot Snapshot() =>
            new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, []);
    }
}
