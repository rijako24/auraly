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

    private static AgentConversationContext Context(IReadOnlyList<PendingCartItem> items, string message)
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            LatestUserMessage = message
        };
        context.Facts[PendingCartCommandMemory.FactKey] = JsonSerializer.Serialize(
            new PendingCartCommandBatch(2, items, DateTime.UtcNow.AddMinutes(30)), JsonOptions);
        return context;
    }

    private static PendingCartItem Item(string text, decimal quantity, CartCommandIssue issue) =>
        new(new CartCommand(CartCommandOperations.Add, text, quantity, null), text, issue, true);

    private static CartCommandIssue Ambiguous(string text, IReadOnlyList<CartCommandCandidate> candidates) =>
        new("product_ambiguous", text, candidates.Select(candidate => candidate.Name).ToList())
        {
            ProductCandidates = candidates
        };

    private static CartCommandCandidate Candidate(string name) => new(name, 1, "COP");
}
