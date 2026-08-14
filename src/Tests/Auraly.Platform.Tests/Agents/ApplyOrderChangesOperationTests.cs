using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Operations.Commerce;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

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

    [Fact]
    public async Task DecimalWeight_IsAppliedWithoutRounding()
    {
        const string productName = "POLLO ENTERO X KG";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["pollo entero"] = [Product(productName)]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "dame un kilo y medio de pollo entero";

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"pollo entero","quantity":1.5,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.applied");
        store.Applied.Should().ContainSingle();
        store.Applied[0].Product!.Name.Should().Be(productName);
        store.Applied[0].Quantity.Should().Be(1.5m);
    }

    [Fact]
    public async Task RequestedProductLabel_IsPreservedBesideResolvedCatalogName()
    {
        const string resolvedName = "PAPA FARM FRITES 3/8 X 2.5 KG";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["papas"] = [Product(resolvedName)]
        });
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, new PresentationSnapshotStore()),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "dame 2 papas";

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"papas","quantity":2,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.applied");
        var item = result.Data.GetProperty("order").GetProperty("items")[0];
        item.GetProperty("requested_name").GetString().Should().Be("papas");
        item.GetProperty("name").GetString().Should().Be(resolvedName);
        session.Facts.Should().ContainKey(CartItemPresentationMemory.FactKey);
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
        store.Applied.Should().HaveCount(2);
        store.Applied.Should().Contain(command =>
            command.Product!.Name == "TROZOS DE PECHUGA DE POLLO" && command.Quantity == 2m);
        store.Applied.Should().Contain(command =>
            command.Product!.Name == "PECHUGA CRIOLLA" && command.Quantity == 1m);
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
    public async Task MedidentalOsseoClarification_ByModelNumberPreservesTwoUnitsWithEmptyPlanBatch()
    {
        const string selected = "Motor de implantes 3G Osseo 200";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(StringComparer.OrdinalIgnoreCase)
        {
            ["osseo"] =
            [
                Product("Motor de implantes 3G Osseo 100"),
                Product(selected)
            ],
            [selected] = [Product(selected)]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store),
            new InMemoryFactsService());
        var session = Session();
        session.LatestUserMessage = "dame dos osseo";

        var ambiguous = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"osseo","quantity":2,"destinationReference":null}]}"""),
            Context(session));

        ambiguous.Code.Should().Be("cart.product_ambiguous");
        session.LatestUserMessage = "3G osseo 200";

        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.Applied.Should().ContainSingle(command =>
            command.Product!.Name == selected && command.Quantity == 2m);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
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
    public async Task AmbiguousReference_AppliesSafeItemsAndKeepsOnlyUnresolvedItemPending()
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

        first.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => command.Product!.Name).Should()
            .BeEquivalentTo("ALA JUMBO MERCAPOLLO", "PECHUGA CAMPOLLO");

        session.LatestUserMessage = "mixto";
        var resumed = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"set_quantity","productText":"pernil","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resumed.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(2);
        store.Applied.Should().ContainSingle();
        store.Applied[0].Operation.Should().Be(CartCommandOperations.Add);
        store.Applied[0].Product!.Name.Should().Be("PERNIL MIXTO MAC POLLO");
        store.Applied[0].Quantity.Should().Be(1m);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }

    [Fact]
    public async Task MultipleUnresolvedItems_AreResolvedIndependentlyWithoutReapplyingSafeItems()
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
        first.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().ContainSingle(command =>
            command.Product!.Name == "PECHUGA CAMPOLLO" && command.Quantity == 5m);

        session.LatestUserMessage = "Cunit x 500";
        var second = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"BUTIFARRA CUNIT X 500 GR","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        second.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(2);
        store.Applied.Should().ContainSingle(command =>
            command.Product!.Name == "BUTIFARRA CUNIT X 500 GR" && command.Quantity == 1m);
        session.Facts["system.pending_cart_commands"].Should().Contain("Long x 10");
        session.Facts["system.pending_cart_commands"].Should().Contain("\"alreadyApplied\":true");
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
        store.ApplyCalls.Should().Be(3);
        store.Applied.Should().ContainSingle();
        store.Applied.Select(command => command.Operation).Should().OnlyContain(operation => operation == CartCommandOperations.Add);
        store.Applied[0].Product!.Name.Should().Be("SALCHICHA LONG X 550GR");
        store.Applied[0].Quantity.Should().Be(1m);
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
        session.Facts["system.catalog_products"] = JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            sequence = 1,
            snapshots = new[]
            {
                new
                {
                    sequence = 1,
                    searchTerms = new[] { "salchicha" },
                    products = new[]
                    {
                        new
                        {
                            productId = (Guid?)null,
                            externalProductId = "CF59",
                            sku = "CF59",
                            name = "SALCHICHA LONG X 550GR",
                            unitPrice = 16023.21m,
                            currency = "COP",
                            stockQuantity = (decimal?)49
                        },
                        new
                        {
                            productId = (Guid?)null,
                            externalProductId = "CF20",
                            sku = "CF20",
                            name = "SALCHICHA LONG X 1100 G X 20UND",
                            unitPrice = 28032.50m,
                            currency = "COP",
                            stockQuantity = (decimal?)113
                        }
                    }
                }
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

        unrelated.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().ContainSingle(command => command.Product!.Name == "PECHUGA CRIOLLA");
        var pending = session.Facts["system.pending_cart_commands"];
        pending.Should().Contain("Long x 10");
        pending.Should().Contain("\"schemaVersion\":2");
        pending.Should().Contain("\"alreadyApplied\":true");
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

        unrelated.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().ContainSingle(command => command.Product!.Name == "PECHUGA CRIOLLA");
        session.Facts["system.pending_cart_commands"].Should().Contain("\"alreadyApplied\":true");

        var repeatedAdd = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}"""),
            Context(session));
        repeatedAdd.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(2);
        store.Applied.Should().ContainSingle(command => command.Product!.Name == "PECHUGA CRIOLLA");

        session.LatestUserMessage = "mercapollo";
        var resolved = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"PERNIL MERCAPOLLO","quantity":1,"destinationReference":null}]}"""),
            Context(session));

        resolved.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(3);
        store.Applied.Should().ContainSingle(command =>
            command.Product!.Name == "PERNIL MERCAPOLLO" && command.Quantity == 1m);
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
        using (var data = JsonDocument.Parse(cancelled.Data.GetRawText()))
        {
            data.RootElement.GetProperty("discarded_items").GetArrayLength().Should().Be(1);
            data.RootElement.GetProperty("discarded_items")[0]
                .GetProperty("product_text").GetString().Should().Be("pernil");
        }
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
    [Fact]
    public async Task RepeatedProviderMessage_ReplaysReceiptWithoutApplyingCartTwice()
    {
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["PECHUGA CRIOLLA"] = [Product("PECHUGA CRIOLLA")]
        });
        var store = new IdempotentStubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store), new InMemoryFactsService());
        var session = Session("wamid.same-message");
        var input = Json("""{"commands":[{"operation":"add","productText":"PECHUGA CRIOLLA","quantity":1,"destinationReference":null}]}""");

        var first = await operation.ExecuteAsync(input, Context(session));
        var replay = await operation.ExecuteAsync(input, Context(session));

        first.Code.Should().Be("cart.applied");
        replay.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Keys.Should().ContainSingle(key => !string.IsNullOrWhiteSpace(key));
    }

    [Fact]
    public async Task RichardFollowUp_ConfirmationAppliesTocinetaWhileUnavailableChorizoRemainsNonBlocking()
    {
        const string selected = "SALSA TOCINETA ADEREZOS 1000GR";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [selected] = [Product(selected)]
        });
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store), new InMemoryFactsService());
        var session = Session("wamid.richard-confirmation");
        session.LatestUserMessage = "Si";
        session.ConversationState.LastBotMessage =
            $"Perfecto, entonces agrego 2 unidades de {selected}, es correcto?";
        session.Facts["system.pending_cart_commands"] = """
            {
              "schemaVersion":2,
              "items":[
                {
                  "command":{"operation":"add","productText":"paquetes de chorizo Salsan","quantity":5,"destinationReference":null},
                  "originalProductText":"paquetes de chorizo Salsan",
                  "issue":{"code":"product_unavailable","productText":"paquetes de chorizo Salsan","candidates":["CHORIZO SALSAN X 20 UND"],"productCandidates":[{"name":"CHORIZO SALSAN X 20 UND","unitPrice":0,"currency":"COP","isAvailable":false}]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                },
                {
                  "command":{"operation":"add","productText":"tocinetas","quantity":2,"destinationReference":null},
                  "originalProductText":"tocinetas",
                  "issue":{"code":"product_ambiguous","productText":"tocinetas","candidates":["SALSA TOCINETA ADEREZOS 1000GR","SALSA TOCINETA ADEREZOS 200 GR"],"productCandidates":[{"name":"SALSA TOCINETA ADEREZOS 1000GR","unitPrice":10,"currency":"COP","isAvailable":true},{"name":"SALSA TOCINETA ADEREZOS 200 GR","unitPrice":5,"currency":"COP","isAvailable":true}]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                }
              ],
              "expiresAtUtc":"2099-01-01T00:00:00Z"
            }
            """;

        var result = await operation.ExecuteAsync(Json("{\"commands\":[]}"), Context(session));

        result.Code.Should().Be("cart.partially_applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Should().ContainSingle(command =>
            command.Product!.Name == selected && command.Quantity == 2m);
        session.Facts["system.pending_cart_commands"].Should().Contain("paquetes de chorizo Salsan");
        session.Facts["system.pending_cart_commands"].Should().Contain("\"originalProductText\":\"tocinetas\"");
        session.Facts["system.pending_cart_commands"].Should().Contain("\"alreadyApplied\":true");
    }
    [Fact]
    public async Task RichardFollowUp_QuantityCorrectionAppliesInsufficientStockAndLeavesOnlyDiscardableIssues()
    {
        const string selected = "JAMON CUNIT X 500GR";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [selected] = [Product(selected)]
        });
        var store = new PresentationSnapshotStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, store), new InMemoryFactsService());
        var session = Session(
            "wamid.richard-stock-correction",
            new PendingCartPolicy
            {
                DiscardOnFinalizeIssueCodes = ["product_unavailable", "product_not_found"]
            });
        session.LatestUserMessage =
            "Bueno, para el que no tiene asistencia suficiente en la Moncuní, dame 5 y listo.";
        session.Facts["system.pending_cart_commands"] = """
            {
              "schemaVersion":2,
              "items":[
                {
                  "command":{"operation":"add","productText":"paquetes de chorizo Salsan","quantity":5,"destinationReference":null},
                  "originalProductText":"paquetes de chorizo Salsan",
                  "issue":{"code":"product_unavailable","productText":"paquetes de chorizo Salsan","candidates":["CHORIZO SALSAN X 20 UND"],"productCandidates":[]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                },
                {
                  "command":{"operation":"add","productText":"ranchera Salsan","quantity":2,"destinationReference":null},
                  "originalProductText":"ranchera Salsan",
                  "issue":{"code":"product_not_found","productText":"ranchera Salsan","candidates":[],"productCandidates":[]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                },
                {
                  "command":{"operation":"add","productText":"jamonada CUNICHEF","quantity":10,"destinationReference":null},
                  "originalProductText":"jamonada CUNICHEF",
                  "issue":{"code":"insufficient_stock","productText":"JAMON CUNIT X 500GR","candidates":["JAMON CUNIT X 500GR"],"productCandidates":[],"maximumCommandQuantity":7},
                  "requiresResolution":true,
                  "alreadyApplied":false
                }
              ],
              "expiresAtUtc":"2099-01-01T00:00:00Z"
            }
            """;

        var result = await operation.ExecuteAsync(
            Json("""{"commands":[{"operation":"add","productText":"jamonada CUNICHEF","quantity":5,"destinationReference":null}]}"""),
            Context(session));

        result.Code.Should().Be("cart.partially_applied");
        var context = result.Error!.Context!.Value;
        context.GetProperty("applied_items").EnumerateArray().Should().ContainSingle(item =>
            item.GetProperty("name").GetString() == selected
            && item.GetProperty("quantity").GetDecimal() == 5m);
        context.GetProperty("can_finalize_with_pending").GetBoolean().Should().BeTrue();
        context.GetProperty("unresolved_item_count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task RichardFollowUp_OnlyDiscardableIssuesRemain_OffersFinalizationInsteadOfMoreClarification()
    {
        const string selected = "TOCINETA CJ 1K";
        var resolver = new StubResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [selected] = [Product(selected)]
        });
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(resolver, new PresentationSnapshotStore()),
            new InMemoryFactsService());
        var session = Session(
            "wamid.richard-ready-to-finish",
            new PendingCartPolicy
            {
                DiscardOnFinalizeIssueCodes = ["product_unavailable", "product_not_found"],
                FinalizeConfirmationPhrases = ["si"]
            });
        session.LatestUserMessage = "Sí, esa";
        session.ConversationState.LastBotMessage =
            $"Perfecto, entonces agrego 3 unidades de {selected}, es correcto?";
        session.Facts["system.pending_cart_commands"] = """
            {
              "schemaVersion":2,
              "items":[
                {
                  "command":{"operation":"add","productText":"paquetes de chorizo Salsan","quantity":5,"destinationReference":null},
                  "originalProductText":"paquetes de chorizo Salsan",
                  "issue":{"code":"product_unavailable","productText":"paquetes de chorizo Salsan","candidates":["CHORIZO SALSAN X 20 UND"],"productCandidates":[{"name":"CHORIZO SALSAN X 20 UND","unitPrice":0,"currency":"COP","isAvailable":false}]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                },
                {
                  "command":{"operation":"add","productText":"ranchera Salsan","quantity":2,"destinationReference":null},
                  "originalProductText":"ranchera Salsan",
                  "issue":{"code":"product_not_found","productText":"ranchera Salsan","candidates":[],"productCandidates":[]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                },
                {
                  "command":{"operation":"add","productText":"tocinetas","quantity":3,"destinationReference":null},
                  "originalProductText":"tocinetas",
                  "issue":{"code":"product_ambiguous","productText":"tocinetas","candidates":["TOCINETA CJ 1K","TOCINETA COMPLETA"],"productCandidates":[{"name":"TOCINETA CJ 1K","unitPrice":10,"currency":"COP","isAvailable":true},{"name":"TOCINETA COMPLETA","unitPrice":12,"currency":"COP","isAvailable":true}]},
                  "requiresResolution":true,
                  "alreadyApplied":false
                }
              ],
              "expiresAtUtc":"2099-01-01T00:00:00Z"
            }
            """;

        var result = await operation.ExecuteAsync(Json("{\"commands\":[]}"), Context(session));

        result.Code.Should().Be("cart.partially_applied");
        var context = result.Error!.Context!.Value;
        context.GetProperty("can_finalize_with_pending").GetBoolean().Should().BeTrue();
        context.GetProperty("unresolved_item_count").GetInt32().Should().Be(2);
        using var pendingDocument = JsonDocument.Parse(session.Facts["system.pending_cart_commands"]);
        var unresolvedNames = pendingDocument.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("requiresResolution").GetBoolean())
            .Select(item => item.GetProperty("originalProductText").GetString())
            .ToList();
        unresolvedNames.Should().BeEquivalentTo(
            "paquetes de chorizo Salsan", "ranchera Salsan");
    }
    [Fact]
    public async Task RichardExactMessage_RecoversAndAppliesAllElevenResolvableProducts()
    {
        const string message = """
            * 10 jamonada CUNICHEF
            * 5 paquetes de chorizo Salsan
            * 2 maíz
            * 3 tocinetas
            * 2 ranchera Salsan, aclarando que no fuera la salchicha pequeña
            * 2 súper ranchera
            * 1 caja de papas
            * 1 ripio
            * 5 chicharrón
            * 1 champiñón
            * 3 leche de coco Kary
            """;
        var signalDefinition = new StageSignalDefinition { Type = "order_changes" };
        var planningContext = new TurnPlanningContext(
            new AgentConfig { Commerce = new CommerceConfig { Enabled = true } },
            new AgentFlowStage(),
            new TurnPlanScope(
                new Dictionary<string, FactSchemaEntry>(),
                new Dictionary<string, StageSignalDefinition>
                {
                    ["order_changes"] = signalDefinition
                }),
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.Parse("2026-07-16T15:46:00-05:00"),
            []);
        var normalized = CommerceTurnPlanSafety.Normalize(new TurnPlan(), planningContext);
        var commands = normalized.Signals.Should().ContainSingle().Subject.Value;
        var candidates = commands.EnumerateArray().ToDictionary(
            command => command.GetProperty("productText").GetString()!,
            command => (IReadOnlyList<ProductReference>)
            [Product(command.GetProperty("productText").GetString()!.ToUpperInvariant())],
            StringComparer.OrdinalIgnoreCase);
        var store = new StubStore();
        var operation = new ApplyOrderChangesOperation(
            new CartCommandBatchProcessor(new StubResolver(candidates), store),
            new InMemoryFactsService());
        var session = Session("wamid.richard-list");
        session.LatestUserMessage = message;

        var result = await operation.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { commands }),
            Context(session));

        result.Code.Should().Be("cart.applied");
        store.ApplyCalls.Should().Be(1);
        store.Applied.Select(command => command.Quantity).Should().Equal(
            10m, 5m, 2m, 3m, 2m, 2m, 1m, 1m, 5m, 1m, 3m);
        store.Applied.Should().HaveCount(11);
        session.Facts.Should().NotContainKey("system.pending_cart_commands");
    }
    private static AgentConversationContext Session(
        string? providerMessageId = null,
        PendingCartPolicy? pendingCart = null) => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new(),
        ProviderMessageId = providerMessageId,
        Config = new AgentConfig
        {
            Commerce = new CommerceConfig
            {
                Enabled = true,
                Conversation = new CommerceConversationPolicy
                {
                    ContextualConfirmationPhrases = ["si", "si esa", "si es esa", "confirmo"],
                    CandidateSelectionPhrases = ["esta", "esa", "primera", "primero"],
                    ClauseSeparators = ["y", "e", "tambien", "ademas"],
                    AdditionalRequestPhrases = ["otra", "otro", "adicional", "mas", "nuevamente", "tambien agrega"]
                },
                PendingCart = pendingCart ?? new PendingCartPolicy()
            }
        }
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

    private sealed class PresentationSnapshotStore : ICartMutationStore
    {
        public Task<OrderSnapshot> GetCurrentAsync(
            AgentConversationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot([]));

        public Task<OrderSnapshot> ApplyAtomicallyAsync(
            AgentConversationContext context,
            IReadOnlyList<ResolvedCartCommand> commands,
            CancellationToken cancellationToken = default)
        {
            var items = commands
                .Where(command => command.Product is not null)
                .Select(command => new OrderItemSnapshot(
                    Guid.NewGuid(),
                    command.Product!.ProductId,
                    command.Product.ExternalProductId,
                    command.Product.Sku,
                    command.Product.Name,
                    command.Quantity ?? 0m,
                    command.Product.UnitPrice,
                    (command.Quantity ?? 0m) * command.Product.UnitPrice))
                .ToList();
            return Task.FromResult(Snapshot(items));
        }

        private static OrderSnapshot Snapshot(IReadOnlyList<OrderItemSnapshot> items) =>
            new(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                items.Sum(item => item.LineTotal),
                0m,
                items.Sum(item => item.LineTotal),
                items);
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
            new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, []);
    }

    private sealed class IdempotentStubStore : ICartMutationStore
    {
        private readonly Dictionary<string, OrderSnapshot> _receipts = new(StringComparer.Ordinal);
        public int ApplyCalls { get; private set; }
        public IReadOnlyCollection<string> Keys => _receipts.Keys;

        public Task<OrderSnapshot> GetCurrentAsync(
            AgentConversationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot());

        public Task<OrderSnapshot> ApplyAtomicallyAsync(
            AgentConversationContext context, IReadOnlyList<ResolvedCartCommand> commands,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The idempotent path must be used.");

        public Task<CartMutationApplyResult> ApplyIdempotentlyAsync(
            AgentConversationContext context, IReadOnlyList<ResolvedCartCommand> commands,
            string? idempotencyKey, CancellationToken cancellationToken = default)
        {
            idempotencyKey.Should().NotBeNullOrWhiteSpace();
            if (_receipts.TryGetValue(idempotencyKey!, out var receipt))
                return Task.FromResult(new CartMutationApplyResult(receipt, true));
            ApplyCalls++;
            var snapshot = Snapshot();
            _receipts[idempotencyKey!] = snapshot;
            return Task.FromResult(new CartMutationApplyResult(snapshot, false));
        }

        private static OrderSnapshot Snapshot() =>
            new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, []);
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
