using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CommerceTurnPlanSafetyTests
{
    [Fact]
    public void CatalogInquiry_ReplacesInventedAddWithCatalogQuery()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("pechuga", 1, "tienes pechuga")),
            Context("¿Tienes pechuga?"));

        normalized.Signals.Should().ContainSingle();
        var signal = normalized.Signals[0];
        signal.Type.Should().Be("catalog_query");
        signal.Value.GetProperty("queries")[0].GetString().Should().Be("pechuga");
        signal.Evidence.Should().Be("tienes pechuga");
    }

    [Fact]
    public void ReplacementCatalogQuery_DefersOnlyTheRemovalOfTheRejectedReference()
    {
        var removals = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new object[]
            {
                new { operation = "remove", productText = "maiz", quantity = (decimal?)null, destinationReference = (string?)null },
                new { operation = "remove", productText = "chicharron", quantity = (decimal?)null, destinationReference = (string?)null }
            }),
            Evidence = "ese maiz no lo quiero, muestrame otros; y saca el chicharron",
            Confidence = 0.98
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(removals, CatalogQuery("maiz", "maiz")),
            Context(
                "ese maiz no lo quiero, muestrame otros; y saca el chicharron",
                currentCartProducts: ["chicharron"]));

        normalized.Signals.Should().HaveCount(2);
        var commands = normalized.Signals.Single(signal => signal.Type == "order_changes").Value;
        commands.GetArrayLength().Should().Be(1);
        commands[0].GetProperty("operation").GetString().Should().Be("remove");
        commands[0].GetProperty("productText").GetString().Should().Be("chicharron");
        normalized.Signals.Should().ContainSingle(signal => signal.Type == "catalog_query");
    }

    [Fact]
    public void CatalogInquiry_WithExplicitMutation_KeepsCartCommand()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("pechuga", 2, "agrega 2 pechugas")),
            Context("¿Tienes pechuga? agrega 2 pechugas"));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void CartMutationAndOpenCategoryRequest_ArePreservedAsIndependentIntents()
    {
        var categories = new PlannedSignal
        {
            Type = "catalog_query",
            Value = JsonSerializer.SerializeToElement(new
            {
                mode = "categories",
                queries = Array.Empty<string>(),
                replacement_reference = (string?)null
            }),
            Evidence = "que otros productos tienes",
            Confidence = 0.98
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("super coco", 2, "dame dos super coco"), categories),
            Context("dame dos super coco y que otros productos tienes"));

        normalized.Signals.Should().HaveCount(2);
        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        normalized.Signals.Single(signal => signal.Type == "catalog_query")
            .Value.GetProperty("mode").GetString().Should().Be("categories");
    }

    [Fact]
    public void CatalogSelectionWithoutQuantity_DropsInventedAddOfOne()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("TROZOS DE PECHUGA DE POLLO", 1, "trozos de pechuga")),
            Context("trozos de pechuga", catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sí, agrégame")]
    [InlineData("agrégame esa")]
    [InlineData("ponme la primera")]
    [InlineData("inclúyela")]
    [InlineData("quiero esa")]
    [InlineData("esa misma")]
    [InlineData("ranchera super x 525 gr x 7 und, agrégame")]
    public void CatalogSelectionWithoutRequestedQuantity_DropsInventedOneEvenWithMutationVerb(string message)
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("SALCHICHA RANCHERA SUPER X 525 GR X 7 UND", 1, message)),
            Context(message, catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }

    [Theory]
    [InlineData("add", 6)]
    [InlineData("set_quantity", 6)]
    public void CatalogSelectionWithoutRequestedQuantity_DropsAnyInventedMutationQuantity(
        string operation,
        decimal inventedQuantity)
    {
        var signal = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new[]
            {
                new
                {
                    operation,
                    productText = "MAIZ SUPER DULCE",
                    quantity = inventedQuantity,
                    destinationReference = (string?)null
                }
            }),
            Evidence = "El maíz super dulce.",
            Confidence = 0.95
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(signal),
            Context("El maíz super dulce.", catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty(
            "a catalog selection does not authorize reusing a previous line quantity");
    }

    [Fact]
    public void CatalogSelectionWithoutQuantity_PreservesIndependentRemovalButDropsInventedReplacement()
    {
        var signal = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new object[]
            {
                new { operation = "set_quantity", productText = "MAIZ SUPER DULCE", quantity = 6m, destinationReference = (string?)null },
                new { operation = "remove", productText = "CHICHARRON CARNUDO", quantity = (decimal?)null, destinationReference = (string?)null }
            }),
            Evidence = "El maíz super dulce y saca el chicharrón.",
            Confidence = 0.95
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(signal),
            Context(
                "El ma\u00EDz super dulce y saca el chicharr\u00F3n.",
                catalogFollowUp: true,
                currentCartProducts: ["CHICHARRON CARNUDO"]));

        var command = normalized.Signals.Should().ContainSingle().Subject.Value
            .EnumerateArray().Should().ContainSingle().Subject;
        command.GetProperty("operation").GetString().Should().Be("remove");
        command.GetProperty("productText").GetString().Should().Be("CHICHARRON CARNUDO");
    }

    [Theory]
    [InlineData("sí, agrégame 3", 3)]
    [InlineData("agrégame una", 1)]
    [InlineData("ponme dos de esa", 2)]
    [InlineData("la primera, 4 unidades", 4)]
    [InlineData("5 de esa", 5)]
    [InlineData("quiero 6", 6)]
    [InlineData("entonces agrega 1.5k", 1.5)]
    [InlineData("entonces agrega 1,5 kg", 1.5)]
    public void CatalogSelectionWithRequestedQuantity_KeepsMutation(string message, decimal quantity)
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("PECHUGA CRIOLLA", quantity, message)),
            Context(message, catalogFollowUp: true));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        normalized.Signals[0].Value[0].GetProperty("quantity").GetDecimal().Should().Be(quantity);
    }

    [Fact]
    public void OfferedProductNotInCart_ConvertsSetQuantityToAdd()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges(
                "PECHUGA CRIOLLA",
                4,
                "Ponme 4 de esa.",
                operation: "set_quantity")),
            Context(
                "Ponme 4 de esa.",
                catalogFollowUp: true,
                offeredProducts: ["PECHUGA CRIOLLA", "PECHUGA MAC POLLO"]));

        normalized.Signals.Single().Value[0]
            .GetProperty("operation").GetString().Should().Be("add");
    }

    [Fact]
    public void OfferedProductAlreadyInCart_KeepsSetQuantity()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges(
                "PECHUGA CRIOLLA",
                2,
                "Quiero 2 en total.",
                operation: "set_quantity")),
            Context(
                "Quiero 2 en total.",
                catalogFollowUp: true,
                offeredProducts: ["PECHUGA CRIOLLA"],
                currentCartProducts: ["PECHUGA CRIOLLA"]));

        normalized.Signals.Single().Value[0]
            .GetProperty("operation").GetString().Should().Be("set_quantity");
    }

    [Fact]
    public void CatalogSelectionWithLeadingQuantity_KeepsCartCommand()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("TROZOS DE PECHUGA DE POLLO", 2, "2 trozos de pechuga")),
            Context("2 trozos de pechuga", catalogFollowUp: true));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void CatalogSelectionNamedWithPackNumbers_DoesNotTreatPresentationAsRequestedQuantity()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("SALCHICHA RANCHERA SUPER X 525 GR X 7 UND", 1, "ranchera super x 525 gr x 7 und")),
            Context("ranchera super x 525 gr x 7 und", catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }


    [Fact]
    public void CatalogSelectionWithConfiguredAdditionalPhrase_KeepsSingleAdditionalMutation()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("PERNIL MERCAPOLLO", 1, "agrega otro pernil mercapollo")),
            Context(
                "agrega otro pernil mercapollo",
                catalogFollowUp: true,
                additionalRequestPhrases: ["otro", "otra"]));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
    }

    [Theory]
    [InlineData("Kellogg's")]
    [InlineData("Zucaritas")]
    public void BareProductWithoutQuantity_IsDiscoveryAndNeverAnInventedAdd(string message)
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges(message, 1m, message)),
            Context(message));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "catalog_query");
        normalized.Signals.Should().NotContain(signal => signal.Type == "order_changes");
        normalized.Signals[0].Value.GetProperty("mode").GetString().Should().Be("search");
        normalized.Signals[0].Value.GetProperty("queries")[0].GetString().Should().Be(message);
    }

    [Fact]
    public void CatalogReadWithQuantity_WinsOverAContradictoryCartClassification()
    {
        const string message = "Tienen 2 Zucaritas?";
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("Zucaritas", 2m, message), CatalogQuery("Zucaritas", null)),
            Context(message));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "catalog_query");
        normalized.Signals.Should().NotContain(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void MutationWhoseEvidenceIsNotInCurrentMessage_IsNeverAuthorized()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("Kellogg's", 1m, "agrega 1 Kellogg's")),
            Context("Kellogg's"));

        normalized.Signals.Should().NotContain(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void SingleProductAndQuantityWithoutVerb_IsAddedImmediately()
    {
        const string message = "3 Zucaritas";
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("Zucaritas", 3m, message)),
            Context(message));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        normalized.Signals[0].Value[0].GetProperty("quantity").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public void QuantityEmbeddedOnlyInProductPresentation_DoesNotAuthorizeThatQuantity()
    {
        const string message = "ZUCARITAS X 7 UND";
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges(message, 7m, message)),
            Context(message, catalogFollowUp: true));

        normalized.Signals.Should().NotContain(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void SetQuantityForANonexistentCartLine_IsNotAuthorized()
    {
        const string message = "deja 3 Zucaritas";
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("Zucaritas", 3m, message, operation: "set_quantity")),
            Context(message));

        normalized.Signals.Should().NotContain(signal => signal.Type == "order_changes");
        normalized.Signals.Should().ContainSingle(signal => signal.Type == "catalog_query");
    }

    [Fact]
    public void RemoveForANonexistentCartLine_IsNotAuthorized()
    {
        var removal = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new[]
            {
                new { operation = "remove", productText = "Zucaritas", quantity = (decimal?)null, destinationReference = (string?)null }
            }),
            Evidence = "quita Zucaritas",
            Confidence = 0.99
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(removal),
            Context("quita Zucaritas"));

        normalized.Signals.Should().BeEmpty();
    }

    [Fact]
    public void ClearForAbsentFact_IsRemovedAsNoOp()
    {
        var plan = new TurnPlan
        {
            Facts =
            [
                new PlannedFactClaim
                {
                    Key = "delivery_reference",
                    Operation = TurnPlanOperations.Clear,
                    Value = JsonSerializer.SerializeToElement<object?>(null),
                    Evidence = "no"
                }
            ],
            Response = new TurnPlanResponseDirective()
        };

        var normalized = CommerceTurnPlanSafety.Normalize(plan, Context("no"));

        normalized.Facts.Should().BeEmpty();
    }

    [InlineData(false)]
    [Theory]
    [InlineData(true)]
    public void ProductListWithTrailingQuantities_PreservesEveryProductAndQuantity(bool catalogFollowUp)
    {
        const string message = "pechuga mac pollo 5, salchicha ranchera super 3, pechuga criolla 1";
        var orderChanges = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new object[]
            {
                new { operation = "add", productText = "pechuga mac pollo", quantity = 5m, destinationReference = (string?)null },
                new { operation = "add", productText = "salchicha ranchera super", quantity = 3m, destinationReference = (string?)null },
                new { operation = "add", productText = "pechuga criolla", quantity = 1m, destinationReference = (string?)null }
            }),
            Evidence = message,
            Confidence = 0.95
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(orderChanges),
            Context(message, catalogFollowUp));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        var commands = normalized.Signals[0].Value;
        commands.GetArrayLength().Should().Be(3);
        commands.EnumerateArray()
            .Select(command => (
                command.GetProperty("productText").GetString(),
                command.GetProperty("quantity").GetDecimal()))
            .Should().Equal(
                ("pechuga mac pollo", 5m),
                ("salchicha ranchera super", 3m),
                ("pechuga criolla", 1m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RichardExactBulletList_WhenPlannerOmitsOrTruncatesSignal_RecoversEveryCommand(bool plannerReturnsOnlyFirst)
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

        var plannerOutput = plannerReturnsOnlyFirst
            ? Plan(OrderChanges("jamonada CUNICHEF", 10m, message))
            : Plan();
        var normalized = CommerceTurnPlanSafety.Normalize(plannerOutput, Context(message));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        normalized.Signals[0].Evidence.Should().Be(message.Trim());
        normalized.Signals[0].Value.EnumerateArray()
            .Select(command => (
                command.GetProperty("productText").GetString(),
                command.GetProperty("quantity").GetDecimal()))
            .Should().Equal(
                ("jamonada CUNICHEF", 10m),
                ("paquetes de chorizo Salsan", 5m),
                ("maíz", 2m),
                ("tocinetas", 3m),
                ("ranchera Salsan, aclarando que no fuera la salchicha pequeña", 2m),
                ("súper ranchera", 2m),
                ("caja de papas", 1m),
                ("ripio", 1m),
                ("chicharrón", 5m),
                ("champiñón", 1m),
                ("leche de coco Kary", 3m));
    }

    [Fact]
    public void CatalogQuestionWithBulletQuantities_DoesNotInventCartMutation()
    {
        const string message = """
            ¿Tienen disponibles estos productos?
            * 2 jamonadas
            * 3 tocinetas
            """;

        var normalized = CommerceTurnPlanSafety.Normalize(Plan(), Context(message));

        normalized.Signals.Should().BeEmpty();
    }

    [Fact]
    public void FinalizationWithoutAuthoritativeCart_IsRemoved()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            PlanWithFinalization(),
            CommerceFinalizationContext(hasCart: false));

        normalized.Facts.Should().BeEmpty();
    }

    [Fact]
    public void FinalizationWithAuthoritativeCart_IsPreserved()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            PlanWithFinalization(),
            CommerceFinalizationContext(hasCart: true));

        normalized.Facts.Should().ContainSingle(fact => fact.Key == "order_finalized");
    }

    [Fact]
    public void SafetyLayer_DoesNotSynthesizeSignalFromAConversationPhrase()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(),
            Context("SOLO RESINA TEGD-MA ME ESTAS COTIZANDO LAMPARA"));

        normalized.Signals.Should().BeEmpty();
    }
    [Fact]
    public void CatalogCorrectionWithoutMutationVerb_DropsInventedRemoval()
    {
        var removal = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new[]
            {
                new
                {
                    operation = "remove",
                    productText = "LAMPARA",
                    quantity = (decimal?)null,
                    destinationReference = (string?)null
                }
            }),
            Evidence = "ME ESTAS COTIZANDO LAMPARA QUE NO PEDI",
            Confidence = 0.98
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(removal),
            Context(
                "SOLO RESINA TEGD-MA. ME ESTAS COTIZANDO LAMPARA QUE NO PEDI.",
                catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }


    private static TurnPlan Plan(params PlannedSignal[] signals) => new()
    {
        Signals = signals,
        Response = new TurnPlanResponseDirective()
    };

    private static PlannedSignal CatalogQuery(string query, string? replacementReference) => new()
    {
        Type = "catalog_query",
        Value = JsonSerializer.SerializeToElement(new
        {
            mode = "search",
            queries = new[] { query },
            replacement_reference = replacementReference
        }),
        Evidence = query,
        Confidence = 0.95
    };

    private static PlannedSignal OrderChanges(
        string productText,
        decimal quantity,
        string evidence,
        string operation = "add") => new()
    {
        Type = "order_changes",
        Value = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                operation,
                productText,
                quantity,
                destinationReference = (string?)null
            }
        }),
        Evidence = evidence,
        Confidence = 0.95
    };

    private static TurnPlan PlanWithFinalization() => new()
    {
        Facts =
        [
            new PlannedFactClaim
            {
                Key = "order_finalized",
                Operation = TurnPlanOperations.Set,
                Value = JsonSerializer.SerializeToElement(true),
                Evidence = "solo eso"
            }
        ],
        Response = new TurnPlanResponseDirective()
    };

    private static TurnPlanningContext CommerceFinalizationContext(bool hasCart)
    {
        var fact = new FactSchemaEntry
        {
            Key = "order_finalized",
            Role = "order.finalized",
            Type = "boolean"
        };
        var config = new AgentConfig
        {
            Commerce = new CommerceConfig { Enabled = true },
            FactSchema = [fact]
        };
        IReadOnlyDictionary<string, JsonElement>? structuredContext = hasCart
            ? new Dictionary<string, JsonElement>
            {
                ["currentCart"] = JsonSerializer.SerializeToElement(new
                {
                    items = new[] { new { name = "PRODUCTO", quantity = 1m } }
                })
            }
            : null;

        return new TurnPlanningContext(
            config,
            new AgentFlowStage(),
            new TurnPlanScope(
                new Dictionary<string, FactSchemaEntry> { [fact.Key] = fact },
                new Dictionary<string, StageSignalDefinition>()),
            new Dictionary<string, string>(),
            "solo eso",
            DateTimeOffset.Parse("2026-07-16T15:00:00-05:00"),
            [],
            structuredContext);
    }

    private static TurnPlanningContext Context(
        string message,
        bool catalogFollowUp = false,
        IReadOnlyList<string>? additionalRequestPhrases = null,
        IReadOnlyList<string>? offeredProducts = null,
        IReadOnlyList<string>? currentCartProducts = null)
    {
        var signals = new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_changes"] = new() { Type = "order_changes" },
            ["catalog_query"] = new() { Type = "catalog_query" }
        };
        Dictionary<string, JsonElement>? structuredContext = catalogFollowUp
            || currentCartProducts is not null
            ? new(StringComparer.OrdinalIgnoreCase)
            : null;
        if (catalogFollowUp)
        {
            structuredContext!["shoppingContext"] = JsonSerializer.SerializeToElement(new
            {
                interaction = new { expected_reply = "catalog_follow_up" },
                offers = offeredProducts is null
                    ? Array.Empty<object>()
                    : new object[]
                {
                        new { products = offeredProducts }
                    }
            });
        }
        if (currentCartProducts is not null)
            structuredContext!["currentCart"] = JsonSerializer.SerializeToElement(new
            {
                items = currentCartProducts.Select(name => new { name, quantity = 1m })
            });

        return new TurnPlanningContext(
            new AgentConfig
            {
                Commerce = new CommerceConfig
                {
                    Conversation = new CommerceConversationPolicy
                    {
                        AdditionalRequestPhrases = additionalRequestPhrases ?? [],
                        QuantityWords = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["un"] = 1m,
                            ["uno"] = 1m,
                            ["una"] = 1m,
                            ["dos"] = 2m,
                            ["tres"] = 3m,
                            ["cuatro"] = 4m,
                            ["cinco"] = 5m,
                            ["seis"] = 6m
                        }
                    }
                }
            },
            new AgentFlowStage(),
            new TurnPlanScope(
                new Dictionary<string, FactSchemaEntry>(),
                signals),
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.Parse("2026-07-14T10:00:00-05:00"),
            [],
            structuredContext);
    }
}
