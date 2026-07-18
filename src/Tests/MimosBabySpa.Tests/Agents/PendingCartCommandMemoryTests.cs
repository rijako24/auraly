using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class PendingCartCommandMemoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MergeResolution_UsesCorrectedQuantityForPreviouslyIdentifiedStockIssue()
    {
        var pending = Item(
            "jamonada CUNICHEF", 10,
            new CartCommandIssue("insufficient_stock", "JAMON CUNIT X 500 GR", [])
            {
                RequestedQuantity = 10,
                AvailableQuantity = 7
            });
        var context = Context([pending], "para el jamón cuny no me dejes 10, dame 6");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, "jamonada CUNICHEF", 6, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.ProductText.Should().Be("JAMON CUNIT X 500 GR");
        result.WorkItems[0].Command.Quantity.Should().Be(6);
        result.RemainingItems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("caja de papa", "papa farm fries", "PAPA FARM FRITES 3/8 X 2.5 KG")]
    [InlineData("ripio", "papa ripio crumar", "PAPA RIPIO KRUMER X 1 KG")]
    public void MergeResolution_SelectsUniquePresentedCandidateDespiteMinorSpellingVariation(
        string original, string clarification, string expected)
    {
        var candidates = original == "ripio"
            ? new[] { Candidate(expected), Candidate("PAPA RIPIO DEL CAMPO X 1 KG") }
            : new[] { Candidate(expected), Candidate("PAPA GOLDEN LONG X 2.5 KG") };
        var pending = Item(original, 1, Ambiguous(original, candidates));
        var context = Context([pending], $"dame 2 {clarification}");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, clarification, 2, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.ProductText.Should().Be(expected);
        result.WorkItems[0].Command.Quantity.Should().Be(2);
        result.RemainingItems.Should().BeEmpty();
    }

    [Fact]
    public void MergeResolution_ReplacesRelatedAmbiguityInsteadOfAppendingDuplicate()
    {
        var pending = Item("tocinetas", 1, Ambiguous("tocinetas",
            [Candidate("SALSA TOCINETA X 200 GR"), Candidate("SALSA TOCINETA X 1000 GR")]));
        var context = Context([pending], "dame 3 de salsa tocineta");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, "salsa tocineta", 3, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].OriginalProductText.Should().Be("tocinetas");
        result.WorkItems[0].Command.ProductText.Should().Be("salsa tocineta");
        result.WorkItems[0].Command.Quantity.Should().Be(3);
        result.RemainingItems.Should().BeEmpty();
    }

    [Fact]
    public void MergeResolution_ResolvesSeveralClarificationsAndKeepsOnlyUnansweredGroups()
    {
        var pending = new[]
        {
            Item("caja de papa", 1, Ambiguous("caja de papa",
                [Candidate("PAPA FARM FRITES 3/8 X 2.5 KG"), Candidate("PAPA GOLDEN X 2.5 KG")])),
            Item("ripio", 1, Ambiguous("ripio",
                [Candidate("PAPA RIPIO KRUMER X 1 KG"), Candidate("PAPA RIPIO DEL CAMPO X 1 KG")])),
            Item("tocinetas", 1, Ambiguous("tocinetas",
                [Candidate("SALSA TOCINETA X 200 GR"), Candidate("SALSA TOCINETA X 1000 GR")]))
        };
        var message = "para papa dame 2 farm fries, para ripio 5 crumar; la tocineta todavía no sé";
        var context = Context(pending, message);

        var result = PendingCartCommandMemory.MergeResolution(context,
        [
            new CartCommand(CartCommandOperations.SetQuantity, "papa farm fries", 2, null),
            new CartCommand(CartCommandOperations.SetQuantity, "papa ripio crumar", 5, null)
        ]);

        result.WorkItems.Select(item => (item.Command.ProductText, item.Command.Quantity)).Should().BeEquivalentTo(
            new (string ProductText, decimal? Quantity)[]
            {
                ("PAPA FARM FRITES 3/8 X 2.5 KG", 2m),
                ("PAPA RIPIO KRUMER X 1 KG", 5m)
            });
        result.RemainingItems.Should().ContainSingle();
        result.RemainingItems[0].OriginalProductText.Should().Be("tocinetas");
    }

    [Fact]
    public void MergeResolution_ConfirmationOfPresentedOptionConsumesThatPendingItem()
    {
        var expected = "LECHE DE COCO KARIX X 400 ML";
        var pending = Item("leche de coco caris", 3,
            Ambiguous("leche de coco caris", [Candidate(expected)]));
        var context = Context([pending], "sí, confirmo que es esta");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.Add, expected, 3, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.ProductText.Should().Be(expected);
        result.RemainingItems.Should().BeEmpty();
    }

    [Fact]
    public void MergeResolution_DoesNotConsumeSalsaClarificationForUnavailableSalsanProduct()
    {
        var pending = new[]
        {
            Item("paquetes de chorizo Salsan", 5,
                new CartCommandIssue("product_unavailable", "paquetes de chorizo Salsan", ["CHORIZO SALSAN"])
                {
                    ProductCandidates = [Candidate("CHORIZO SALSAN X 20 UND")]
                }),
            Item("tocinetas", 3, Ambiguous("tocinetas",
                [Candidate("SALSA TOCINETA X 200 GR"), Candidate("SALSA TOCINETA X 1000 GR")]))
        };
        var context = Context(pending, "para las tocinetas dame 3 de salsa tocineta");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, "salsa tocineta", 3, null)]);

        result.RemainingItems.Should().ContainSingle(item => item.OriginalProductText == "paquetes de chorizo Salsan");
        result.WorkItems.Should().ContainSingle(item => item.OriginalProductText == "tocinetas");
    }

    [Fact]
    public void MergeResolution_UpdatesAlreadyAppliedProductByItsStoredIdentity()
    {
        var applied = new PendingCartItem(
            new CartCommand(CartCommandOperations.Add, "PAPA RIPIO KRUMER", 1, null),
            "ripio", null, false, true);
        var unresolved = Item("tocinetas", 3, Ambiguous("tocinetas",
            [Candidate("SALSA TOCINETA X 200 GR"), Candidate("SALSA TOCINETA X 1000 GR")]));
        var context = Context([applied, unresolved], "para ripio dame papa ripio crumar, me das 5");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.Add, "papa ripio crumar", 5, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.Operation.Should().Be(CartCommandOperations.SetQuantity);
        result.WorkItems[0].Command.ProductText.Should().Be("PAPA RIPIO KRUMER");
        result.WorkItems[0].Command.Quantity.Should().Be(5);
        result.RemainingItems.Should().ContainSingle(item => item.OriginalProductText == "tocinetas");
    }

    [Fact]
    public void MergeResolution_DoesNotAcceptPresentationInventedByPlanner()
    {
        var selected = "SALSA TOCINETA ADEREZOS 1000GR";
        var pending = Item("tocinetas", 3, Ambiguous("tocinetas",
            [Candidate(selected), Candidate("SALSA TOCINETA ADEREZOS 200 GR")]));
        var context = Context([pending], "para el jamón no me dejes 10, dame 6; para tocinetas dame 3 salsa tocineta; champiñón lata 400 dame 2; la leche sí, sí es esa");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, selected, 3, null)]);

        result.WorkItems.Should().BeEmpty();
        result.RemainingItems.Should().ContainSingle();
        result.RemainingItems[0].OriginalProductText.Should().Be("tocinetas");
    }

    [Fact]
    public void MergeResolution_AcceptsPresentationExplicitlyGroundedBySize()
    {
        var selected = "SALSA TOCINETA ADEREZOS 1000GR";
        var pending = Item("tocinetas", 3, Ambiguous("tocinetas",
            [Candidate(selected), Candidate("SALSA TOCINETA ADEREZOS 200 GR")]));
        var context = Context([pending], "para las tocinetas dame 3 de la salsa de 1000 gramos");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, selected, 3, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.ProductText.Should().Be(selected);
        result.RemainingItems.Should().BeEmpty();
    }

    [Fact]
    public void MergeResolution_MatchesStockCorrectionAgainstRecognizedProductName()
    {
        var issue = new CartCommandIssue("insufficient_stock", "JAMON CUNIT X 500GR", [])
            { RequestedQuantity = 10, AvailableQuantity = 7 };
        var pending = Item("jamonada CUNICHEF", 10, issue);
        var context = Context([pending], "para el jamón cuny dame 6");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, "JAMON CUNIT X 500GR", 6, null)]);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].Command.Quantity.Should().Be(6);
        result.RemainingItems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Si")]
    [InlineData("si, esa")]
    [InlineData("sí es esa")]
    [InlineData("correcto")]
    public void IsContextualConfirmation_RecognizesShortAffirmativeReferences(string message)
    {
        PendingCartCommandMemory.IsContextualConfirmation(message, TestCommerce().Conversation).Should().BeTrue();
    }
    [Fact]
    public void IsContextualConfirmation_UsesConfiguredVocabularyInsteadOfEngineLanguage()
    {
        var policy = new CommerceConversationPolicy
        {
            ContextualConfirmationPhrases = ["yes that one"]
        };

        PendingCartCommandMemory.IsContextualConfirmation("Yes, that one!", policy).Should().BeTrue();
        PendingCartCommandMemory.IsContextualConfirmation("si esa", policy).Should().BeFalse();
    }
    [Fact]
    public void PrimaryIssue_PrioritizesPendingItemNamedInLatestMessage()
    {
        var pending = new[]
        {
            Item("paquetes de chorizo Salsan", 5,
                new CartCommandIssue("product_unavailable", "paquetes de chorizo Salsan", ["CHORIZO SALSAN"])
                {
                    ProductCandidates = [Candidate("CHORIZO SALSAN X 20 UND")]
                }),
            Item("tocinetas", 3, Ambiguous("tocinetas",
                [Candidate("SALSA TOCINETA ADEREZOS 200 GR"), Candidate("SALSA TOCINETA ADEREZOS 1000GR")]))
        };

        var issue = PendingCartCommandMemory.PrimaryIssue(
            pending, "Para las tocinetas agrega dos salsas de tocineta");

        issue.Code.Should().Be("product_ambiguous");
        issue.ProductText.Should().Be("tocinetas");
    }

    [Fact]
    public void MergeResolution_AmbiguousRefinementUpdatesQuantityWithoutInventingPresentation()
    {
        var selected = "SALSA TOCINETA ADEREZOS 1000GR";
        var pending = Item("tocinetas", 3, Ambiguous("tocinetas",
            [Candidate(selected), Candidate("SALSA TOCINETA ADEREZOS 200 GR")]));
        var context = Context([pending], "Dame 2 salsa tocineta");

        var result = PendingCartCommandMemory.MergeResolution(context,
            [new CartCommand(CartCommandOperations.SetQuantity, selected, 2, null)]);

        result.WorkItems.Should().BeEmpty();
        result.RemainingItems.Should().ContainSingle();
        result.RemainingItems[0].Command.Quantity.Should().Be(2);
        result.RemainingItems[0].Issue!.Code.Should().Be("product_ambiguous");
    }

    [Fact]
    public void MergeResolution_BareConfirmationUsesOnlyCandidateProposedByPreviousAssistant()
    {
        var selected = "SALSA TOCINETA ADEREZOS 1000GR";
        var pending = new[]
        {
            Item("paquetes de chorizo Salsan", 5,
                new CartCommandIssue("product_unavailable", "paquetes de chorizo Salsan", ["CHORIZO SALSAN"])
                {
                    ProductCandidates = [Candidate("CHORIZO SALSAN X 20 UND")]
                }),
            Item("tocinetas", 2, Ambiguous("tocinetas",
                [Candidate(selected), Candidate("SALSA TOCINETA ADEREZOS 200 GR")]))
        };
        var context = Context(pending, "Si");
        context.ConversationState.LastBotMessage =
            "Perfecto, entonces agrego 2 unidades de SALSA TOCINETA ADEREZOS 1000GR, es correcto?";

        var result = PendingCartCommandMemory.MergeResolution(context, []);

        result.WorkItems.Should().ContainSingle();
        result.WorkItems[0].OriginalProductText.Should().Be("tocinetas");
        result.WorkItems[0].Command.ProductText.Should().Be(selected);
        result.WorkItems[0].Command.Quantity.Should().Be(2);
        result.RemainingItems.Should().ContainSingle(item =>
            item.OriginalProductText == "paquetes de chorizo Salsan");
    }
    [Fact]
    public void PreserveCatalogAmbiguity_ReplacesRejectedCartProductWithExplicitOfferedProduct()
    {
        const string message = "Quiero maiz super dulce, dame 5";
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            LatestUserMessage = message,
            ConversationState = new()
            {
                LastBotMessage = "MAIZ CONGELADO 1 K\nMAIZ SUPER DULCE x500gr\nSALSA MAIZ DULCE X 1 KG"
            },
            Config = new AgentConfig { Commerce = TestCommerce() }
        };
        context.Facts[CatalogOfferMemory.FactKey] = JsonSerializer.Serialize(
            new CatalogOfferState(2, 1,
            [
                new CatalogOfferSnapshot(1, DateTime.UtcNow, ["maiz"],
                [
                    new ProductCandidate(null, null, null, "MAIZ CONGELADO 1 K", 10_000m),
                    new ProductCandidate(null, null, null, "MAIZ SUPER DULCE x500gr", 8_000m),
                    new ProductCandidate(null, null, null, "SALSA MAIZ DULCE X 1 KG", 12_000m)
                ], "maiz")
            ]), JsonOptions);
        context.Facts[CartItemPresentationMemory.FactKey] = JsonSerializer.Serialize(
            new CartItemPresentationState(1,
            [
                new CartItemPresentationEntry(
                    null, null, null, "MAIZ CONGELADO 1 K", "maíz")
            ]), JsonOptions);

        var result = ProductSelectionMemory.PreserveCatalogAmbiguity(
            context,
            message,
            [new CartCommand(CartCommandOperations.SetQuantity, "maíz", 5, null)]);

        result.Should().HaveCount(2);
        result[0].Operation.Should().Be(CartCommandOperations.Remove);
        result[0].ProductText.Should().Be("MAIZ CONGELADO 1 K");
        result[1].Operation.Should().Be(CartCommandOperations.Add);
        result[1].ProductText.Should().Be("MAIZ SUPER DULCE x500gr");
        result[1].Quantity.Should().Be(5);
    }
    [Theory]
    [InlineData("Quiero maiz dulce, dame 5", "maiz dulce")]
    [InlineData("Sí, dame 5", "maiz")]
    public void PreserveCatalogAmbiguity_AmbiguousReplacementNeverUpdatesRejectedLine(
        string message,
        string expectedRefinement)
    {
        var context = ReplacementContext(
            message,
            "MAIZ CONGELADO X 1 KG",
            "maíz",
            "MAIZ SUPER DULCE X 500 GR",
            "SALSA DE MAIZ DULCE X 1 KG");

        var result = ProductSelectionMemory.PreserveCatalogAmbiguity(
            context,
            message,
            [new CartCommand(CartCommandOperations.SetQuantity, "maíz", 5, null)]);

        result.Should().ContainSingle();
        result[0].Operation.Should().Be(CartCommandOperations.Add);
        result[0].ProductText.Should().Be(expectedRefinement);
        result.Should().NotContain(command => command.Operation == CartCommandOperations.Remove);
    }

    [Fact]
    public void PreserveCatalogAmbiguity_SelectingSameResolvedProductChangesQuantityWithoutReplacement()
    {
        const string selected = "MAIZ SUPER DULCE X 500 GR";
        const string message = "Quiero maiz super dulce, dame 5";
        var context = ReplacementContext(
            message,
            selected,
            "maíz",
            selected,
            "SALSA DE MAIZ DULCE X 1 KG");

        var result = ProductSelectionMemory.PreserveCatalogAmbiguity(
            context,
            message,
            [new CartCommand(CartCommandOperations.SetQuantity, "maíz", 5, null)]);

        result.Should().ContainSingle();
        result[0].Operation.Should().Be(CartCommandOperations.SetQuantity);
        result[0].ProductText.Should().Be(selected);
        result[0].Quantity.Should().Be(5);
    }

    private static AgentConversationContext ReplacementContext(
        string message,
        string resolvedProduct,
        string requestedProduct,
        params string[] offeredProducts)
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            LatestUserMessage = message,
            ConversationState = new()
            {
                LastBotMessage = string.Join('\n', offeredProducts)
            },
            Config = new AgentConfig { Commerce = TestCommerce() }
        };
        context.Facts[CatalogOfferMemory.FactKey] = JsonSerializer.Serialize(
            new CatalogOfferState(2, 1,
            [
                new CatalogOfferSnapshot(
                    1,
                    DateTime.UtcNow,
                    ["maiz"],
                    offeredProducts.Select(name =>
                        new ProductCandidate(null, null, null, name, 10_000m)).ToList(),
                    "maiz")
            ]), JsonOptions);
        context.Facts[CartItemPresentationMemory.FactKey] = JsonSerializer.Serialize(
            new CartItemPresentationState(1,
            [
                new CartItemPresentationEntry(
                    null, null, null, resolvedProduct, requestedProduct)
            ]), JsonOptions);
        return context;
    }
    private static AgentConversationContext Context(IReadOnlyList<PendingCartItem> items, string message)
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            LatestUserMessage = message,
            ConversationState = new(),
            Config = new AgentConfig { Commerce = TestCommerce() }
        };
        context.Facts[PendingCartCommandMemory.FactKey] = JsonSerializer.Serialize(
            new PendingCartCommandBatch(2, items, DateTime.UtcNow.AddMinutes(30)), JsonOptions);
        return context;
    }

    private static CommerceConfig TestCommerce() => new()
    {
        Enabled = true,
        Conversation = new CommerceConversationPolicy
        {
            ContextualConfirmationPhrases =
                ["si", "si esa", "si es esa", "confirmo", "correcto"],
            CandidateSelectionPhrases =
                ["esta", "esa", "primera", "primero", "segunda", "segundo"],
            ClauseSeparators = ["y", "e", "tambien", "ademas"],
            AdditionalRequestPhrases = ["otra", "otro", "adicional", "mas", "nuevamente"],
            QuantityWords = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["uno"] = 1m,
                ["dos"] = 2m,
                ["tres"] = 3m,
                ["cuatro"] = 4m,
                ["cinco"] = 5m,
                ["seis"] = 6m
            }
        },
        Matching = new ProductMatchingPolicy
        {
            CandidateMentionSimilarity = 0.8d,
            PendingReferenceSimilarity = 0.78d,
            CandidateSelectionSimilarity = 0.6d
        }
    };
    private static PendingCartItem Item(string text, decimal quantity, CartCommandIssue issue) =>
        new(new CartCommand(CartCommandOperations.Add, text, quantity, null), text, issue, true);

    private static CartCommandIssue Ambiguous(string text, IReadOnlyList<CartCommandCandidate> candidates) =>
        new("product_ambiguous", text, candidates.Select(candidate => candidate.Name).ToList())
        {
            ProductCandidates = candidates
        };

    private static CartCommandCandidate Candidate(string name) => new(name, 1, "COP");
}
