using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CartCommandBatchProcessorTests
{
    [Fact]
    public async Task Apply_ResolvesEveryProductBeforeWritingTheBatch()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["papas"] = [Product("Papas")],
            ["tocinetas"] = [Product("Tocinetas")]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [Add("papas", 2), Add("tocinetas", 3)]);

        result.Success.Should().BeTrue();
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().HaveCount(2);
        store.Applied.Select(command => command.Quantity).Should().Equal(2m, 3m);
    }

    [Fact]
    public async Task Apply_WritesNothing_WhenAnyProductIsAmbiguous()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["papas"] = [Product("Papas")],
            ["vino"] = [Product("Vino tinto"), Product("Vino blanco")]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [Add("papas", 2), Add("vino", 1)]);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_RejectsConflictingCommandsForSameProductBeforeResolution()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>());
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [Add("Papas", 2), Add("papás", 3)]);

        result.Code.Should().Be("cart.conflicting_commands");
        resolver.Calls.Should().Be(0);
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_SetQuantityForProductAbsentFromCart_ResolvesItAsAdd()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["papas"] = [Product("Papas")]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [new CartCommand(CartCommandOperations.SetQuantity, "papas", 4, null)]);

        result.Success.Should().BeTrue();
        store.Applied.Should().ContainSingle();
        store.Applied[0].Operation.Should().Be(CartCommandOperations.Add);
        store.Applied[0].Quantity.Should().Be(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public async Task Apply_AddWithoutPositiveQuantity_IsRejectedBeforeCatalogOrWrite(double? quantity)
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>());
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [new CartCommand(CartCommandOperations.Add, "papas", (decimal?)quantity, null)]);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("cart.invalid_input");
        resolver.Calls.Should().Be(0);
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_WithMoreThanOneOrderGroup_RejectsWholeBatchBeforeResolution()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>());
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [
                new CartCommand(CartCommandOperations.Add, "papas", 2, "Calle 10"),
                new CartCommand(CartCommandOperations.Add, "tocinetas", 3, "Carrera 20")
            ]);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("cart.multiple_destinations");
        result.Issues.Should().ContainSingle();
        result.Issues[0].Candidates.Should().BeEquivalentTo("Calle 10", "Carrera 20");
        resolver.Calls.Should().Be(0);
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_AfterCustomerChoosesOneDeliveryAddress_AppliesEntireBatchToSingleCart()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["papas"] = [Product("Papas")],
            ["tocinetas"] = [Product("Tocinetas")]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);
        var context = new AgentConversationContext
        {
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["delivery_address"] = "Calle 10"
            }
        };

        var result = await processor.ApplyAsync(
            context,
            [
                new CartCommand(CartCommandOperations.Add, "papas", 2, "Calle 10"),
                new CartCommand(CartCommandOperations.Add, "tocinetas", 3, "Carrera 20")
            ]);

        result.Success.Should().BeTrue();
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().HaveCount(2);
    }
    [Fact]
    public async Task Apply_RejectsWholeBatchBeforeWriting_WhenRequestedQuantityExceedsStock()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["papas"] = [Product("Papas", stock: 5)],
            ["tocinetas"] = [Product("Tocinetas", stock: 2)]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [Add("papas", 2), Add("tocinetas", 3)]);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("cart.insufficient_stock");
        result.Issues.Should().ContainSingle();
        result.Issues[0].ProductText.Should().Be("Tocinetas");
        result.Issues[0].RequestedQuantity.Should().Be(3);
        result.Issues[0].AvailableQuantity.Should().Be(2);
        result.Issues[0].MaximumCommandQuantity.Should().Be(2);
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_AccountsForExistingCartQuantity_WhenCheckingAdditionalStock()
    {
        var productId = Guid.NewGuid();
        var product = Product("Pechuga Campollo", stock: 5) with { ProductId = productId };
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["pechuga campollo"] = [product]
        });
        var current = new OrderSnapshot(
            Guid.NewGuid(), OrderStatus.Draft, "COP", 30, 0, 0, 30,
            [new OrderItemSnapshot(Guid.NewGuid(), productId, product.ExternalProductId, product.Sku, product.Name, 3, 10, 30)]);
        var store = new StubStore(current);
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [Add("pechuga campollo", 3)]);

        result.Code.Should().Be("cart.insufficient_stock");
        result.Issues[0].RequestedQuantity.Should().Be(6);
        result.Issues[0].AvailableQuantity.Should().Be(5);
        result.Issues[0].ExistingCartQuantity.Should().Be(3);
        result.Issues[0].MaximumCommandQuantity.Should().Be(2);
        store.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_AllowsQuantityExactlyEqualToAvailableStock()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["alas"] = [Product("Alas", stock: 2)]
        });
        var store = new StubStore(EmptySnapshot());
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(new AgentConversationContext(), [Add("alas", 2)]);

        result.Success.Should().BeTrue();
        store.ApplyCalls.Should().Be(1);
    }

    [Fact]
    public async Task Apply_RejectsIncreasingExistingItemAboveStockBeforeWriting()
    {
        var product = Product("PECHUGA CAMPOLLO", stock: 4);
        var current = new OrderSnapshot(
            Guid.NewGuid(), OrderStatus.Draft, "COP", 20, 0, 0, 20,
            [new OrderItemSnapshot(Guid.NewGuid(), null, product.ExternalProductId, product.Sku, product.Name, 2, 10, 20)]);
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["pechugas campollo"] = [product]
        });
        var store = new StubStore(current);
        var processor = new CartCommandBatchProcessor(resolver, store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [new CartCommand(CartCommandOperations.SetQuantity, "pechugas campollo", 5, null)]);

        result.Code.Should().Be("cart.insufficient_stock");
        result.Issues[0].RequestedQuantity.Should().Be(5);
        result.Issues[0].AvailableQuantity.Should().Be(4);
        result.Issues[0].MaximumCommandQuantity.Should().Be(4);
        store.ApplyCalls.Should().Be(0);
    }
    [Fact]
    public async Task Apply_RemovesCartItemUsingPartialPluralReference()
    {
        var current = new OrderSnapshot(
            Guid.NewGuid(), OrderStatus.Draft, "COP", 20, 0, 0, 20,
            [new OrderItemSnapshot(Guid.NewGuid(), null, "EXT-1", "PC", "PECHUGA CRIOLLA", 2, 10, 20)]);
        var store = new StubStore(current);
        var processor = new CartCommandBatchProcessor(
            new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>()),
            store);

        var result = await processor.ApplyAsync(
            new AgentConversationContext(),
            [new CartCommand(CartCommandOperations.Remove, "criollas", null, null)]);

        result.Success.Should().BeTrue();
        store.Applied.Should().ContainSingle();
        store.Applied[0].Operation.Should().Be(CartCommandOperations.Remove);
        store.Applied[0].OrderItemId.Should().Be(current.Items[0].OrderItemId);
    }
    [Fact]
    public async Task Apply_SupportsMultiTurnCartLifecycle_AddRemoveAddAndChangeQuantity()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>
        {
            ["pechuga campollo"] = [Product("PECHUGA CAMPOLLO", 10)],
            ["ala jumbo"] = [Product("ALA JUMBO MERCAPOLLO", 10)],
            ["alas jumbo"] = [Product("ALA JUMBO MERCAPOLLO", 10)],
            ["pernil"] = [Product("PERNIL CAMPOLLO", 10)]
        });
        var store = new StatefulStore();
        var processor = new CartCommandBatchProcessor(resolver, store);
        var context = new AgentConversationContext();

        (await processor.ApplyAsync(context, [Add("pechuga campollo", 2), Add("ala jumbo", 1)]))
            .Success.Should().BeTrue();
        (await processor.ApplyAsync(context,
            [new CartCommand(CartCommandOperations.Remove, "pechugas campollo", null, null)]))
            .Success.Should().BeTrue();
        (await processor.ApplyAsync(context, [Add("pernil", 3)]))
            .Success.Should().BeTrue();
        (await processor.ApplyAsync(context,
            [new CartCommand(CartCommandOperations.SetQuantity, "alas jumbo", 2, null)]))
            .Success.Should().BeTrue();

        var final = await store.GetCurrentAsync(context);
        final.Items.Should().HaveCount(2);
        final.Items.Should().Contain(item => item.ProductName == "ALA JUMBO MERCAPOLLO" && item.Quantity == 2);
        final.Items.Should().Contain(item => item.ProductName == "PERNIL CAMPOLLO" && item.Quantity == 3);
    }
    private static CartCommand Add(string product, decimal quantity) =>
        new(CartCommandOperations.Add, product, quantity, null);

    private static ProductReference Product(string name, decimal? stock = 100) =>
        new(null, name, name, name, null, null, 10, "COP", stock);

    private static OrderSnapshot EmptySnapshot() =>
        new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, 0, []);

    private sealed class StubResolver : ICartProductResolver
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ProductReference>> _products;

        public StubResolver(IReadOnlyDictionary<string, IReadOnlyList<ProductReference>> products) => _products = products;
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ProductReference>> FindAsync(AgentConversationContext context, string productText, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_products.TryGetValue(productText, out var products) ? products : (IReadOnlyList<ProductReference>)[]);
        }
    }

    private sealed class StatefulStore : ICartMutationStore
    {
        private readonly List<OrderItemSnapshot> _items = [];

        public Task<OrderSnapshot> GetCurrentAsync(AgentConversationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot());

        public Task<OrderSnapshot> ApplyAtomicallyAsync(
            AgentConversationContext context,
            IReadOnlyList<ResolvedCartCommand> commands,
            CancellationToken cancellationToken = default)
        {
            foreach (var command in commands)
            {
                if (command.Operation == CartCommandOperations.Add)
                {
                    var product = command.Product!;
                    var existing = _items.FirstOrDefault(item =>
                        item.ExternalProductId == product.ExternalProductId || item.Sku == product.Sku);
                    if (existing is null)
                    {
                        _items.Add(new OrderItemSnapshot(
                            Guid.NewGuid(), product.ProductId, product.ExternalProductId, product.Sku,
                            product.Name, command.Quantity!.Value, product.UnitPrice,
                            command.Quantity.Value * product.UnitPrice));
                    }
                    else
                    {
                        Replace(existing, existing.Quantity + command.Quantity!.Value);
                    }
                    continue;
                }

                var item = _items.Single(value => value.OrderItemId == command.OrderItemId);
                if (command.Operation == CartCommandOperations.Remove)
                    _items.Remove(item);
                else
                    Replace(item, command.Quantity!.Value);
            }

            return Task.FromResult(Snapshot());
        }

        private void Replace(OrderItemSnapshot item, decimal quantity)
        {
            var index = _items.IndexOf(item);
            _items[index] = item with { Quantity = quantity, LineTotal = quantity * item.UnitPrice };
        }

        private OrderSnapshot Snapshot()
        {
            var total = _items.Sum(item => item.LineTotal);
            return new OrderSnapshot(Guid.NewGuid(), OrderStatus.Draft, "COP", total, 0, 0, total, _items.ToList());
        }
    }
    private sealed class StubStore : ICartMutationStore
    {
        private readonly OrderSnapshot _current;

        public StubStore(OrderSnapshot current) => _current = current;
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<ResolvedCartCommand> Applied { get; private set; } = [];

        public Task<OrderSnapshot> GetCurrentAsync(AgentConversationContext context, CancellationToken cancellationToken = default) => Task.FromResult(_current);

        public Task<OrderSnapshot> ApplyAtomicallyAsync(AgentConversationContext context, IReadOnlyList<ResolvedCartCommand> commands, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            Applied = commands;
            return Task.FromResult(_current);
        }
    }
}
