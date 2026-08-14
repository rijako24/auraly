using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Operations.Commerce;
using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Catalog;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class CommerceCartLifecycleRegressionTests
{
    [Fact]
    public async Task LongOrder_CanReplaceRemoveSearchAskQuantityAndContinueWithoutCorruptingCart()
    {
        var products = new Dictionary<string, ProductReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["jamonada CUNICHEF"] = Product("JAMON CUNIT X 500 GR", "JA1"),
            ["maíz"] = Product("MAIZ CONGELADO X 1 KG", "MA1"),
            ["tocinetas"] = Product("TOCINETA AHUMADA X 500 GR", "TO1"),
            ["súper ranchera"] = Product("SALCHICHA RANCHERA SUPER X 525 GR", "RA1"),
            ["caja de papas"] = Product("PAPA FARM FRITES X 2.5 KG", "PA1"),
            ["ripio"] = Product("PAPA RIPIO X 1 KG", "PR1"),
            ["chicharrón"] = Product("CHICHARRON X 500 GR", "CH1"),
            ["champiñón"] = Product("CHAMPINON LAMINADO X 400 GR", "CP1"),
            ["MAIZ SUPER DULCE X 500 GR"] = Product("MAIZ SUPER DULCE X 500 GR", "MA2"),
            ["PECHUGA CRIOLLA"] = Product("PECHUGA CRIOLLA", "PE2")
        };
        var resolver = new FlowResolver(products);
        var store = new StatefulStore();
        var facts = new InMemoryFactsService();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store), facts);
        var session = Session();

        session.LatestUserMessage = """
            * 10 jamonada CUNICHEF
            * 2 maíz
            * 3 tocinetas
            * 2 súper ranchera
            * 1 caja de papas
            * 1 ripio
            * 5 chicharrón
            * 1 champiñón
            """;
        var initial = await ExecuteAsync(operation, session,
        [
            Add("jamonada CUNICHEF", 10),
            Add("maíz", 2),
            Add("tocinetas", 3),
            Add("súper ranchera", 2),
            Add("caja de papas", 1),
            Add("ripio", 1),
            Add("chicharrón", 5),
            Add("champiñón", 1)
        ]);

        initial.Code.Should().Be("cart.applied");
        store.Items.Should().HaveCount(8);
        Item(store, "MAIZ CONGELADO X 1 KG").Quantity.Should().Be(2);

        var cornOptions = new[]
        {
            products["maíz"],
            products["MAIZ SUPER DULCE X 500 GR"],
            Product("SALSA DE MAIZ DULCE X 1 KG", "MA3")
        };
        session.LatestUserMessage = "Ese maíz ya no me convence; muéstrame las otras opciones";
        await Auraly.Platform.Application.Agents.Operations.Support.CatalogOfferMemory.RememberAsync(
            facts, session, cornOptions, ["maiz"], CancellationToken.None, "maíz");
        session.ConversationState.LastBotMessage = string.Join('\n', cornOptions.Select(product => product.Name));

        session.LatestUserMessage = "Quiero maíz super dulce, dame 5";
        var replacement = await ExecuteAsync(operation, session,
            [new CartCommand(CartCommandOperations.SetQuantity, "maíz", 5, null)]);

        replacement.Code.Should().Be("cart.applied");
        store.Items.Should().HaveCount(8);
        store.Items.Should().NotContain(item => item.ProductName == "MAIZ CONGELADO X 1 KG");
        Item(store, "MAIZ SUPER DULCE X 500 GR").Quantity.Should().Be(5);
        var replacementItems = replacement.Data.GetProperty("applied_items");
        replacementItems.GetArrayLength().Should().Be(2);
        replacementItems[0].GetProperty("removed").GetBoolean().Should().BeTrue();
        replacementItems[1].GetProperty("removed").GetBoolean().Should().BeFalse();

        session.LatestUserMessage = "Sácame el chicharrón del pedido";
        var removed = await ExecuteAsync(operation, session,
            [new CartCommand(CartCommandOperations.Remove, "chicharrón", null, null)]);

        removed.Code.Should().Be("cart.applied");
        store.Items.Should().NotContain(item => item.ProductName == "CHICHARRON X 500 GR");
        var removedItem = removed.Data.GetProperty("applied_items").EnumerateArray().Should().ContainSingle().Subject;
        removedItem.GetProperty("removed").GetBoolean().Should().BeTrue();

        var pechugaOptions = new[]
        {
            Product("TROZOS DE PECHUGA DE POLLO", "PE1"),
            products["PECHUGA CRIOLLA"]
        };
        session.LatestUserMessage = "¿Qué pechugas tienes?";
        await Auraly.Platform.Application.Agents.Operations.Support.CatalogOfferMemory.RememberAsync(
            facts, session, pechugaOptions, ["pechuga"], CancellationToken.None);
        session.ConversationState.LastBotMessage = string.Join('\n', pechugaOptions.Select(product => product.Name));
        var countBeforeQuantity = store.Items.Count;

        var noQuantity = CommerceTurnPlanSafety.Normalize(
            Plan(Add("PECHUGA CRIOLLA", 1), "Sí, agrégame"),
            PlanningContext(session, "Sí, agrégame"));

        noQuantity.Signals.Should().BeEmpty(
            "the assistant must ask for quantity instead of inventing one unit");
        store.Items.Should().HaveCount(countBeforeQuantity);

        var withQuantity = CommerceTurnPlanSafety.Normalize(
            Plan(Add("PECHUGA CRIOLLA", 3), "Sí, agrégame 3"),
            PlanningContext(session, "Sí, agrégame 3"));
        var plannedCommands = withQuantity.Signals.Should().ContainSingle().Subject.Value;
        session.LatestUserMessage = "Sí, agrégame 3";
        var pechugaAdded = await ExecuteRawAsync(operation, session, plannedCommands);

        pechugaAdded.Code.Should().Be("cart.applied");
        Item(store, "PECHUGA CRIOLLA").Quantity.Should().Be(3);
        Item(store, "MAIZ SUPER DULCE X 500 GR").Quantity.Should().Be(5,
            "an unrelated catalog query must not consume the previous corn replacement");

        session.LatestUserMessage = "Déjame solo 2 pechugas criollas";
        await ExecuteAsync(operation, session,
            [new CartCommand(CartCommandOperations.SetQuantity, "pechuga criolla", 2, null)]);
        Item(store, "PECHUGA CRIOLLA").Quantity.Should().Be(2);

        session.LatestUserMessage = "Agrégame otra pechuga criolla";
        await ExecuteAsync(operation, session, [Add("PECHUGA CRIOLLA", 1)]);
        Item(store, "PECHUGA CRIOLLA").Quantity.Should().Be(3);

        store.Items.Should().HaveCount(8);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Fact]
    public async Task UnresolvedProduct_DoesNotReplayOrBlockIndependentRemovalsAndAdds()
    {
        var tocineta = Product("TOCINETA AHUMADA X 500 GR", "TO1");
        var pechuga = Product("PECHUGA CRIOLLA", "PE2");
        var resolver = new FlowResolver(new Dictionary<string, ProductReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["tocinetas"] = tocineta,
            ["PECHUGA CRIOLLA"] = pechuga
        });
        var store = new StatefulStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store), new InMemoryFactsService());
        var session = Session();

        session.LatestUserMessage = "Dame 2 tocinetas y 4 del producto que no existe";
        var initial = await ExecuteAsync(operation, session,
        [
            Add("tocinetas", 2),
            Add("producto que no existe", 4)
        ]);

        initial.Code.Should().Be("cart.partially_applied");
        store.Items.Should().ContainSingle();
        session.Facts["system.pending_cart_commands"].Should().Contain("producto que no existe");

        session.LatestUserMessage = "Saca las tocinetas";
        var removed = await ExecuteAsync(operation, session,
            [new CartCommand(CartCommandOperations.Remove, "tocinetas", null, null)]);

        removed.Code.Should().Be("cart.partially_applied");
        store.Items.Should().BeEmpty();
        removed.Error!.Context!.Value.GetProperty("display_applied_items")[0]
            .GetProperty("removed").GetBoolean().Should().BeTrue();
        session.Facts["system.pending_cart_commands"].Should().Contain("producto que no existe");

        session.LatestUserMessage = "Agrégame 2 pechugas criollas";
        var added = await ExecuteAsync(operation, session, [Add("PECHUGA CRIOLLA", 2)]);

        added.Code.Should().Be("cart.partially_applied");
        store.Items.Should().ContainSingle(item =>
            item.ProductName == "PECHUGA CRIOLLA" && item.Quantity == 2);
        added.Error!.Context!.Value.GetProperty("display_applied_items")
            .EnumerateArray().Should().ContainSingle(item =>
                item.GetProperty("name").GetString() == "PECHUGA CRIOLLA");
        session.Facts["system.pending_cart_commands"].Should().Contain("producto que no existe");
    }

    private static AgentConversationContext Session() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new(),
        Config = new AgentConfig
        {
            Commerce = new CommerceConfig
            {
                Enabled = true,
                Conversation = new CommerceConversationPolicy
                {
                    ProductReplacementRules =
                    [
                        new CommercePhraseRule
                        {
                            Phrase = "no lo quiero",
                            Match = CommercePhraseMatchModes.Contains
                        }
                    ]
                },
                Matching = new ProductMatchingPolicy { ExactNameDominanceMinimumMatches = 2 }
            }
        }
    };

    private static TurnPlan Plan(CartCommand command, string evidence) => new()
    {
        Signals =
        [
            new PlannedSignal
            {
                Type = "order_changes",
                Value = JsonSerializer.SerializeToElement(new[]
                {
                    new
                    {
                        operation = command.Operation,
                        productText = command.ProductText,
                        quantity = command.Quantity,
                        destinationReference = command.DestinationReference
                    }
                }),
                Evidence = evidence,
                Confidence = 1
            }
        ],
        Response = new TurnPlanResponseDirective()
    };

    private static TurnPlanningContext PlanningContext(
        AgentConversationContext session,
        string message)
    {
        var fragment = CommerceSelectionPlanningContextEnricher.Build(
            session.Facts, session.Config!.Commerce)!;
        return new TurnPlanningContext(
            session.Config,
            new AgentFlowStage(),
            new TurnPlanScope(
                new Dictionary<string, FactSchemaEntry>(),
                new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["order_changes"] = new() { Type = "order_changes" },
                    ["catalog_query"] = new() { Type = "catalog_query" }
                }),
            session.Facts,
            message,
            DateTimeOffset.Parse("2026-07-17T12:00:00-05:00"),
            [],
            new Dictionary<string, JsonElement> { [fragment.Key] = fragment.Value });
    }

    private static Task<OperationOutcome> ExecuteAsync(
        ApplyOrderChangesOperation operation,
        AgentConversationContext session,
        IReadOnlyList<CartCommand> commands) =>
        operation.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { commands }),
            Context(session));

    private static Task<OperationOutcome> ExecuteRawAsync(
        ApplyOrderChangesOperation operation,
        AgentConversationContext session,
        JsonElement commands)
    {
        using var document = JsonDocument.Parse($"{{\"commands\":{commands.GetRawText()}}}");
        return operation.ExecuteAsync(document.RootElement.Clone(), Context(session));
    }

    private static OperationContext Context(AgentConversationContext session) => new()
    {
        BusinessId = session.BusinessId,
        ConversationId = session.ConversationId,
        ConversationState = session.ConversationState,
        Session = session,
        Facts = session.Facts
    };

    private static CartCommand Add(string product, decimal quantity) =>
        new(CartCommandOperations.Add, product, quantity, null);

    private static ProductReference Product(string name, string sku) =>
        new(null, sku, sku, name, null, null, 10_000m, "COP", 100m);

    private static OrderItemSnapshot Item(StatefulStore store, string name) =>
        store.Items.Should().ContainSingle(item => item.ProductName == name).Subject;

    private sealed class FlowResolver : ICartProductResolver
    {
        private readonly Dictionary<string, ProductReference> _products;

        public FlowResolver(IReadOnlyDictionary<string, ProductReference> products)
        {
            _products = new Dictionary<string, ProductReference>(products, StringComparer.OrdinalIgnoreCase);
            foreach (var product in products.Values)
            {
                _products[product.Name] = product;
                if (!string.IsNullOrWhiteSpace(product.Sku))
                    _products[product.Sku] = product;
            }
        }

        public Task<IReadOnlyList<ProductReference>> FindAsync(
            AgentConversationContext context,
            string productText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductReference>>(
                _products.TryGetValue(productText, out var product) ? [product] : []);
        public Task<ProductResolution> ResolveAsync(
            AgentConversationContext context,
            string productText,
            CancellationToken cancellationToken = default)
        {
            if (!_products.TryGetValue(productText, out var product))
                return Task.FromResult(ProductResolution.NotFound(productText));
            return Task.FromResult(new ProductResolution(
                ProductResolutionStatus.Resolved,
                product,
                [new ProductResolutionCandidate(product, 1d, ProductMatchSource.Catalog)],
                productText));
        }
    }

    private sealed class StatefulStore : ICartMutationStore
    {
        private readonly List<OrderItemSnapshot> _items = [];
        public IReadOnlyList<OrderItemSnapshot> Items => _items;

        public Task<OrderSnapshot> GetCurrentAsync(
            AgentConversationContext context,
            CancellationToken cancellationToken = default) =>
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
            _items[index] = item with
            {
                Quantity = quantity,
                LineTotal = quantity * item.UnitPrice
            };
        }

        private OrderSnapshot Snapshot()
        {
            var total = _items.Sum(item => item.LineTotal);
            return new OrderSnapshot(
                Guid.NewGuid(), OrderStatus.Draft, "COP", total, 0, total, _items.ToList());
        }
    }

    private sealed class InMemoryFactsService : IConversationFactsService
    {
        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(
            Guid conversationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationFactRecord>>([]);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            Guid conversationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(
            Guid conversationId, Guid businessId, string key, string value,
            bool rememberAcrossRequests = false, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyBatchAsync(
            Guid conversationId, Guid businessId,
            IReadOnlyDictionary<string, string?> mutations,
            IReadOnlySet<string> rememberAcrossRequests,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(
            Guid conversationId, IReadOnlyCollection<string> persistentKeys,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ClearFieldsAsync(
            Guid conversationId, IReadOnlyCollection<string> fields,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
