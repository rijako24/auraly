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
    private static CartCommand Add(string product, decimal quantity) =>
        new(CartCommandOperations.Add, product, quantity, null);

    private static ProductReference Product(string name) =>
        new(null, name, name, name, null, null, 10, "COP", 100);

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
