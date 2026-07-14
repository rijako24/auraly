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
    public async Task UniqueAllTermMatches_ApplyWholeBatchWithoutCreatingPendingAmbiguity()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pechuga mac pollo"] =
            [
                Product("PECHUGA MAC POLLO"),
                Product("PECHUGA CRIOLLA"),
                Product("PECHUGA MERCAPOLLO")
            ],
            ["salchicha ranchera super"] =
            [
                Product("SALCHICHA RANCHERA SUPER X 525 GR X 7 UND"),
                Product("SALCHICHA LONG X 550GR"),
                Product("SALCHICHA CAZADORA")
            ]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "2 pechuga mac pollo y 3 salchicha ranchera super";

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pechuga mac pollo","quantity":2,"destinationReference":null},{"operation":"add","productText":"salchicha ranchera super","quantity":3,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("PECHUGA MAC POLLO", 2m),
            ("SALCHICHA RANCHERA SUPER X 525 GR X 7 UND", 3m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Theory]
    [InlineData("2 trozos de pechuga y una criolla")]
    [InlineData("dos trozos de pechuga y una criolla")]
    public async Task ExplicitQuantityInClarification_ReplacesPendingDefaultQuantity(string clarification)
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pechuga"] =
            [
                Product("TROZOS DE PECHUGA DE POLLO"),
                Product("PECHUGA MAC POLLO"),
                Product("PECHUGA CRIOLLA"),
                Product("PECHUGA MERCAPOLLO")
            ],
            ["TROZOS DE PECHUGA DE POLLO"] = [Product("TROZOS DE PECHUGA DE POLLO")],
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "tienes pechuga";

        var ambiguous = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pechuga","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        ambiguous.Code.Should().Be("cart.product_ambiguous");
        session.LatestUserMessage = clarification;

        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"TROZOS DE PECHUGA DE POLLO","quantity":2,"destinationReference":null},{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("TROZOS DE PECHUGA DE POLLO", 2m),
            ("PECHUGA CRIOLLA", 1m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Fact]
    public async Task ClarificationWithoutExplicitQuantity_PreservesPendingQuantity()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")],
            ["PERNIL MERCAPOLLO"] = [Product("PERNIL MERCAPOLLO")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "agrega 3 perniles";

        var ambiguous = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":3,"destinationReference":null}]}"""),
            Context(session));

        ambiguous.Code.Should().Be("cart.product_ambiguous");
        session.LatestUserMessage = "mercapollo";

        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PERNIL MERCAPOLLO","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.Applied.Should().ContainSingle();
        store.Applied[0].Product!.Name.Should().Be("PERNIL MERCAPOLLO");
        store.Applied[0].Quantity.Should().Be(3);
    }

    [Fact]
    public async Task QuantityForAnotherProduct_DoesNotReplacePendingQuantity()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")],
            ["PERNIL MERCAPOLLO"] = [Product("PERNIL MERCAPOLLO")],
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "agrega 3 perniles";

        var ambiguous = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":3,"destinationReference":null}]}"""),
            Context(session));

        ambiguous.Code.Should().Be("cart.product_ambiguous");
        session.LatestUserMessage = "mercapollo y agrega una pechuga criolla";

        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PERNIL MERCAPOLLO","quantity":1,"destinationReference":null},{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("PERNIL MERCAPOLLO", 3m),
            ("PECHUGA CRIOLLA", 1m));
    }

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
            Json("""{"commands":[{"operation":"set_quantity","productText":"SALCHICHA LONG X 550GR","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resumed.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => command.Operation).Should().OnlyContain(operation => operation == CartCommandOperations.Add);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("BUTIFARRA CUNIT X 500 GR", 1m),
            ("SALCHICHA LONG X 550GR", 1m),
            ("PECHUGA CAMPOLLO", 5m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Theory]
    [InlineData("""{"commands":[]}""")]
    [InlineData("""{"commands":[{"operation":"add","productText":"Long x 10","quantity":1,"destinationReference":null}]}""")]
    public async Task GuardSignal_ResolvesPendingSelectionFromLatestMessageAndCatalog(string incomingJson)
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Long x 10"] = [],
            ["SALCHICHA LONG X 550GR"] = [Product("SALCHICHA LONG X 550GR")]
        });
        var store = new StubStore();
        var facts = new InMemoryFactsService();
        var operation = new ApplyOrderChangesOperation(new CartCommandBatchProcessor(resolver, store), facts);
        var session = Session();

        var first = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"Long x 10","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        first.Code.Should().Be("cart.product_not_found");
        store.ApplyCalls.Should().Be(0);
        session.Facts.Should().ContainKey("system.pending_cart_commands");
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

        var resumed = await operation.ExecuteAsync(Json(incomingJson), Context(session));

        resumed.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().ContainSingle();
        store.Applied[0].Product!.Name.Should().Be("SALCHICHA LONG X 550GR");
        store.Applied[0].Quantity.Should().Be(1m);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Fact]
    public async Task PendingWithoutCandidates_DoesNotRewriteUnrelatedIndependentAdd()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Long x 10"] = [],
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new StubStore();
        var facts = new InMemoryFactsService();
        var operation = new ApplyOrderChangesOperation(new CartCommandBatchProcessor(resolver, store), facts);
        var session = Session();

        var first = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"Long x 10","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        first.Code.Should().Be("cart.product_not_found");
        session.Facts["system.catalog_products"] = JsonSerializer.Serialize(new[]
        {
            new
            {
                product_id = (Guid?)null,
                external_product_id = "P1",
                sku = "P1",
                name = "PECHUGA CRIOLLA",
                unit_price = 14033.67m,
                currency = "COP",
                stock_quantity = (decimal?)100
            }
        });
        session.LatestUserMessage = "tambien agrega una pechuga criolla";

        var unrelated = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        unrelated.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);
        var pending = session.Facts["system.pending_cart_commands"];
        pending.Should().Contain("Long x 10");
        pending.Should().Contain("PECHUGA CRIOLLA");
        pending.Should().Contain(""""ambiguousProductText":"Long x 10"""");
    }
    [Fact]
    public async Task PendingAmbiguity_DoesNotUseUnrelatedCatalogProductAsResolution_AndPreservesLaterAdds()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")],
            ["PERNIL MERCAPOLLO"] = [Product("PERNIL MERCAPOLLO")],
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();

        var ambiguous = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":1,"destinationReference":null}]}"""),
            Context(session));
        ambiguous.Code.Should().Be("cart.product_ambiguous");

        session.LatestUserMessage = "tambien agrega una pechuga criolla";
        var unrelated = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        unrelated.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);
        session.Facts["system.pending_cart_commands"].Should().Contain("PECHUGA CRIOLLA");

        var repeatedAdd = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));
        repeatedAdd.Code.Should().Be("cart.product_ambiguous");

        session.LatestUserMessage = "mercapollo";
        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PERNIL MERCAPOLLO","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => (command.Product!.Name, command.Quantity)).Should().Equal(
            ("PERNIL MERCAPOLLO", 1m),
            ("PECHUGA CRIOLLA", 2m));
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }
    [Fact]
    public async Task ExpandedCatalogName_IsReducedToSupportedWords_WhenUserReferenceStillMatchesSeveralOffers()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")],
            ["PERNIL MERCAPOLLO"] = [Product("PERNIL MERCAPOLLO")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "agrega un pernil";
        session.Facts["system.catalog_products"] = """
            {"schemaVersion":2,"sequence":1,"snapshots":[{"sequence":1,"searchTerms":["pollo"],"products":[{"externalProductId":"1","name":"PERNIL MERCAPOLLO","unitPrice":1,"currency":"COP"},{"externalProductId":"2","name":"PERNIL CAMPOLLO","unitPrice":2,"currency":"COP"}]}]}
            """;

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PERNIL MERCAPOLLO","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);
        session.Facts["system.pending_cart_commands"].Should().Contain("\"ambiguousProductText\":\"pernil\"");
    }

    [Fact]
    public async Task StopWords_DoNotSelectAnOtherwiseAmbiguousCatalogVariant()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pechuga pollo"] = [Product("TROZOS DE PECHUGA DE POLLO"), Product("PECHUGA MAC POLLO")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "agrega pechuga de pollo";
        session.Facts["system.catalog_products"] = """
            {"schemaVersion":2,"sequence":1,"snapshots":[{"sequence":1,"searchTerms":["pechuga"],"products":[{"externalProductId":"1","name":"TROZOS DE PECHUGA DE POLLO","unitPrice":1,"currency":"COP"},{"externalProductId":"2","name":"PECHUGA MAC POLLO","unitPrice":2,"currency":"COP"}]}]}
            """;

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"TROZOS DE PECHUGA DE POLLO","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.product_ambiguous");
        store.ApplyCalls.Should().Be(0);
        session.Facts["system.pending_cart_commands"].Should().Contain(""""ambiguousProductText":"pechuga pollo"""");
    }
    [Fact]
    public async Task EmptyBatch_RePresentsPendingAmbiguityWithoutMutatingTheCart()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();

        (await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":1,"destinationReference":null}]}"""),
            Context(session))).Code.Should().Be("cart.product_ambiguous");

        session.LatestUserMessage = "eso es todo";
        var blockedFinalization = await operation.ExecuteAsync(
            Json("""{"commands":[]}"""),
            Context(session));

        blockedFinalization.Code.Should().Be("cart.product_ambiguous");
        blockedFinalization.Error!.Context!.Value.GetProperty("product_options").GetArrayLength().Should().Be(2);
        store.ApplyCalls.Should().Be(0);
        session.Facts.Should().ContainKey("system.pending_cart_commands");
    }
    [Fact]
    public async Task PendingAdd_CanBeCancelledWithoutMutatingTheCart()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pernil"] = [Product("PERNIL MERCAPOLLO"), Product("PERNIL CAMPOLLO")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();

        (await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pernil","quantity":1,"destinationReference":null}]}"""),
            Context(session))).Code.Should().Be("cart.product_ambiguous");

        session.LatestUserMessage = "mejor no agregues el pernil";
        var cancelled = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"cancel_pending","productText":"pernil","quantity":null,"destinationReference":null}]}"""),
            Context(session));

        cancelled.Success.Should().BeTrue();
        cancelled.Code.Should().Be("cart.pending_cancelled");
        store.ApplyCalls.Should().Be(0);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }
    [Theory]
    [InlineData("{\"schemaVersion\":1,\"commands\":[],\"ambiguousProductText\":\"pernil\",\"productCandidates\":[],\"expiresAtUtc\":\"2000-01-01T00:00:00Z\"}")]
    [InlineData("not-json")]
    public async Task ExpiredOrMalformedPendingMemory_IsClearedAndDoesNotBlockNewCommands(string pendingJson)
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.Facts["system.pending_cart_commands"] = pendingJson;
        session.LatestUserMessage = "agrega una pechuga criolla";

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
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
