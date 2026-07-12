using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ApplyOrderChangesOperationTests
{
    [Fact]
    public async Task AmbiguousReference_UsesLiteralReplyAndPreservesEntireOriginalBatch_WhenLlmReferenceDegrades()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL MIXTO MAC POLLO")],
            ["PERNIL MIXTO MAC POLLO"] = [Product("PERNIL MIXTO MAC POLLO")],
            ["alas"] = [Product("ALA JUMBO MERCAPOLLO")],
            ["pechuga"] = [Product("PECHUGA CAMPOLLO")]
        });
        var store = new StubStore();
        var facts = new InMemoryFactsService();
        var operation = new ApplyOrderChangesOperation(new CartCommandBatchProcessor(resolver, store), facts);
        var session = Session();

        var first = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":1,"destinationReference":null},{"operation":"add","productText":"alas","quantity":2,"destinationReference":null},{"operation":"add","productText":"pechuga","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        first.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);

        session.LatestUserMessage = "mixto";
        var resumed = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"set_quantity","productText":"pernil","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resumed.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied[0].Operation.Should().Be(CartCommandOperations.Add);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("PERNIL MIXTO MAC POLLO", 1m),
            ("ALA JUMBO MERCAPOLLO", 2m),
            ("PECHUGA CAMPOLLO", 1m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Fact]
    public async Task NotFoundAfterAmbiguity_MovesPendingReference_AndCatalogSelectionRestoresWholeBatch()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["butifarra"] = [Product("BUTIFARRA CUNIT X 500 GR"), Product("BUTIFARRA RED X 900 GR")],
            ["BUTIFARRA CUNIT X 500 GR"] = [Product("BUTIFARRA CUNIT X 500 GR")],
            ["Long x 10"] = [],
            ["SALCHICHA LONG X 550GR"] = [Product("SALCHICHA LONG X 550GR")],
            ["pechuga"] = [Product("PECHUGA CAMPOLLO")]
        });
        var store = new StubStore();
        var facts = new InMemoryFactsService();
        var operation = new ApplyOrderChangesOperation(new CartCommandBatchProcessor(resolver, store), facts);
        var session = Session();

        var first = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"butifarra","quantity":1,"destinationReference":null},{"operation":"add","productText":"Long x 10","quantity":1,"destinationReference":null},{"operation":"add","productText":"pechuga","quantity":5,"destinationReference":null}]}"""),
            Context(session));
        first.Code.Should().Be("cart.product_ambiguous");

        session.LatestUserMessage = "Cunit x 500";
        var second = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"BUTIFARRA CUNIT X 500 GR","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        second.Code.Should().Be("cart.product_not_found");
        session.Facts["system.pending_cart_commands"].Should().Contain("Long x 10");
        session.Facts["system.pending_cart_commands"].Should().NotContain("\"ambiguousProductText\":\"butifarra\"");
        session.Facts["system.catalog_products"] = JsonSerializer.Serialize(new[]
        {
            new
            {
                product_id = (Guid?)null,
                external_product_id = "CF59",
                sku = "CF59",
                name = "SALCHICHA LONG X 550GR",
                unit_price = 16023.21m,
                currency = "COP",
                stock_quantity = (decimal?)49
            },
            new
            {
                product_id = (Guid?)null,
                external_product_id = "CF20",
                sku = "CF20",
                name = "SALCHICHA LONG X 1100 G X 20UND",
                unit_price = 28032.50m,
                currency = "COP",
                stock_quantity = (decimal?)113
            }
        });

        session.LatestUserMessage = "Salchicha long x 550 gr";
        var resumed = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"SALCHICHA LONG X 550GR","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resumed.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("BUTIFARRA CUNIT X 500 GR", 1m),
            ("SALCHICHA LONG X 550GR", 1m),
            ("PECHUGA CAMPOLLO", 5m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }
    private static AgentConversationContext Session() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new()
    };

    private static OperationContext Context(AgentConversationContext session) => new()
    {
        BusinessId = session.BusinessId,
        ConversationId = session.ConversationId,
        ConversationState = session.ConversationState,
        Session = session,
        Facts = session.Facts
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ProductReference Product(string name) =>
        new(null, name, name, name, null, null, 10, "COP", 100);

    private sealed class StubResolver : ICartProductResolver
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ProductReference>> _products;
        public StubResolver(IReadOnlyDictionary<string, IReadOnlyList<ProductReference>> products) => _products = products;

        public Task<IReadOnlyList<ProductReference>> FindAsync(
            AgentConversationContext context,
            string productText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_products.TryGetValue(productText, out var products) ? products : (IReadOnlyList<ProductReference>)[]);
    }

    private sealed class StubStore : ICartMutationStore
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
            new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, 0, []);
    }

    private sealed class InMemoryFactsService : IConversationFactsService
    {
        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(Guid conversationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationFactRecord>>([]);
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetAsync(Guid conversationId, Guid businessId, string key, string value, bool rememberAcrossRequests = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyBatchAsync(Guid conversationId, Guid businessId, IReadOnlyDictionary<string, string?> mutations, IReadOnlySet<string> rememberAcrossRequests, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(Guid conversationId, IReadOnlyCollection<string> persistentKeys, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> ClearFieldsAsync(Guid conversationId, IReadOnlyCollection<string> fields, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
