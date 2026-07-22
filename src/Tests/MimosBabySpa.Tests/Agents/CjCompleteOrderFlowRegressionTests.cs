using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

/// <summary>
/// Regression conversations for CJ. Every test drives a complete, stateful order
/// through the real deterministic coordinator and the real cart batch operation.
/// The planner and external providers are scripted so failures are reproducible.
/// </summary>
public sealed class CjCompleteOrderFlowRegressionTests
{
    [Fact]
    public async Task CashOrder_FromIdentificationToExplicitConfirmation_CreatesExactlyOnce()
    {
        var flow = new CjConversationHarness();

        await flow.TurnAsync("Richard", Facts(("customer_name", "Richard")));
        await flow.TurnAsync("restaurate", Facts(("customer_type", "Restaurante")));
        var order = await flow.TurnAsync(
            "Dame 2 trozos de pechuga, 1 Mac Pollo, 2 tocinetas Nojos y 3 El Coleo",
            Changes(
                Add("trozos de pechuga", 2),
                Add("pechuga mac pollo", 1),
                Add("tocineta nojos", 2),
                Add("tocineta el coleo", 3)));

        order.Success.Should().BeTrue(string.Join("; ", order.Errors));
        flow.Cart.Items.Should().HaveCount(4);
        flow.Cart.Quantity("TROZOS DE PECHUGA DE POLLO").Should().Be(2);

        var finalized = await flow.TurnAsync("Por ahora solo eso", Facts(("order_finalized", true)));
        finalized.CurrentStageId.Should().Be("order_data");

        await flow.TurnAsync("Domicilio", Facts(("delivery_method", "domicilio")));
        var delivery = await flow.TurnAsync(
            "Calle 5N y el teléfono es 3012926660",
            Facts(
                ("delivery_address", "Calle 5N"),
                ("delivery_phone", "3012926660")));
        delivery.CurrentStageId.Should().Be(
            "payment_method", JsonSerializer.Serialize(delivery.Facts));
        flow.Facts["delivery_address"].Should().Be("Calle 5N");
        flow.Facts["delivery_phone"].Should().Be("3012926660");

        var summary = await flow.TurnAsync("Efectivo", Facts(("payment_method", "efectivo")));
        AssertCheckoutAwaitingConfirmation(flow, summary, "order_checkout_no_payment");

        var completed = await flow.TurnAsync("Sí, confirmo", Facts(("customer_confirmed", true)));

        completed.Success.Should().BeTrue(string.Join("; ", completed.Errors));
        completed.RequestCompleted.Should().BeTrue();
        completed.Sequences.Should().NotContain("order_created_customer");
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.Checkout.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(4);

        var duplicate = await flow.TurnAsync("Sí", EmptyPlan());
        duplicate.RequestCompleted.Should().BeFalse("the creation action was idempotently skipped");
        flow.CreateOrder.CallCount.Should().Be(1, "a repeated confirmation must not duplicate an order");
    }

    [Fact]
    public async Task CardTerminalOrder_UsesTheSameSummaryAndVerbalConfirmationGateAsCash()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Agrega 3 rancheras y 2 cajas de papa",
            Changes(Add("ranchera super", 3), Add("papa farm frites", 2)));
        await flow.TurnAsync("Eso sería todo", Facts(("order_finalized", true)));
        await flow.TurnAsync(
            "Es domicilio en la carrera 19 # 8-20, recibe María, celular 3001112233",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Carrera 19 # 8-20"),
                ("delivery_recipient_name", "María"),
                ("delivery_phone", "3001112233")));

        var summary = await flow.TurnAsync("Con datáfono", Facts(("payment_method", "datafono")));

        AssertCheckoutAwaitingConfirmation(flow, summary, "order_checkout_card_terminal");
        flow.CreateOrder.CallCount.Should().Be(0);

        var completed = await flow.TurnAsync("Sí, está correcto", Facts(("customer_confirmed", true)));

        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.LastPaymentMethod.Should().Be("datafono");
    }

    [Fact]
    public async Task TransferOrder_PresentsOfficialCheckoutButNeverCreatesFromAChatConfirmation()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Ponme 2 jamonadas Cunichef y 1 papa ripio",
            Changes(Add("jamonada cunichef", 2), Add("papa ripio", 1)));
        await flow.TurnAsync("No quiero agregar nada más", Facts(("order_finalized", true)));
        await flow.TurnAsync(
            "Domicilio, calle 8 # 12-40, teléfono 3014445566",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 8 # 12-40"),
                ("delivery_phone", "3014445566")));

        var pendingPayment = await flow.TurnAsync(
            "Transferencia",
            Facts(("payment_method", "transferencia")));

        pendingPayment.CurrentStageId.Should().Be("order_confirmation");
        pendingPayment.Facts.Should().Contain("order_checkout_presented", "true");
        flow.Checkout.CallCount.Should().Be(1);
        flow.Checkout.LastOutcomeCode.Should().Be("order.checkout_pending_manual_payment");
        flow.CreateOrder.CallCount.Should().Be(0);
        pendingPayment.RequestCompleted.Should().BeFalse();

        var unsafeConfirmation = await flow.TurnAsync(
            "Sí, ya transferí",
            Facts(("customer_confirmed", true)));

        unsafeConfirmation.RequestCompleted.Should().BeFalse();
        flow.CreateOrder.CallCount.Should().Be(0,
            "transfer orders are created only by the manual payment workflow");
    }

    [Fact]
    public async Task ExploratoryOrder_WithQuestionsAmbiguityReplacementRemovalAndReview_CompletesWithoutStateLoss()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();

        var recipe = await flow.TurnAsync(
            "Quiero preparar pechuga con tocineta y algunas salsas",
            Recipe("pechuga con tocineta"));
        recipe.Trace.Should().Contain(trace => trace.OperationId == "commerce.search_recipes");
        recipe.Trace.Should().Contain(trace => trace.OperationId == "commerce.search_products");
        flow.Cart.Items.Should().BeEmpty();

        var initial = await flow.TurnAsync(
            "Agrega 2 trozos de pechuga, 2 maíz, 3 tocinetas, 1 chicharrón y 4 del que no existe",
            Changes(
                Add("trozos de pechuga", 2),
                Add("maíz tierno", 2),
                Add("tocineta", 3),
                Add("chicharrón", 1),
                Add("producto inexistente", 4)));

        initial.Trace.Should().ContainSingle(trace =>
            trace.OperationId == "commerce.apply_order_changes"
            && trace.OutcomeCode == "cart.partially_applied",
            JsonSerializer.Serialize(initial.Trace.Select(trace =>
                new { trace.OperationId, trace.OutcomeCode, trace.SkipReason })));
        flow.Cart.Items.Should().HaveCount(3);
        flow.Facts["system.pending_cart_commands"].Should()
            .Contain("tocineta")
            .And.Contain("producto inexistente");

        var clarified = await flow.TurnAsync(
            "La referencia es TOCINETA NOJOS X 1000GR, quiero 3",
            Changes(Add("TOCINETA NOJOS X 1000GR", 3)));
        clarified.Trace.Should().Contain(trace => trace.OperationId == "commerce.apply_order_changes");
        flow.Cart.Items.Should().ContainSingle(
            item => item.ProductName == "TOCINETA NOJOS X 1000GR",
            JsonSerializer.Serialize(clarified.Trace.Select(trace =>
                new { trace.OutcomeCode, trace.ArgumentsJson })));
        flow.Cart.Quantity("TOCINETA NOJOS X 1000GR").Should().Be(
            3,
            JsonSerializer.Serialize(clarified.Trace.Select(trace => trace.OutcomeCode)));
        flow.Facts["system.pending_cart_commands"].Should().Contain("producto inexistente");

        var cornOptions = await flow.TurnAsync(
            "Ese maíz no lo quiero, ¿qué otros maíces tienes?",
            Catalog(["maíz"], "MAIZ TIERNO CONGELADO"));
        cornOptions.Trace.Should().ContainSingle(trace =>
            trace.OperationId == "commerce.search_products"
            && trace.OutcomeCode == "products.found");
        flow.Cart.Quantity("MAIZ TIERNO CONGELADO").Should().Be(2);

        var selectionOnly = await flow.TurnAsync("El maíz súper dulce", EmptyPlan());
        selectionOnly.Trace.Where(trace => !trace.Skipped).Should().BeEmpty(
            "a catalog choice without quantity must not retry an unrelated pending product");
        flow.Cart.Quantity("MAIZ TIERNO CONGELADO").Should().Be(2,
            "choosing an offered product without quantity is not a mutation");

        await flow.TurnAsync(
            "Ponme 5 de ese",
            Changes(SetQuantity("MAIZ SUPER DULCE X 500 GR", 5)));
        flow.Cart.Items.Should().NotContain(item => item.ProductName == "MAIZ TIERNO CONGELADO");
        flow.Cart.Quantity("MAIZ SUPER DULCE X 500 GR").Should().Be(5);

        var breastQuestion = await flow.TurnAsync(
            "¿Qué otras pechugas tienes?",
            Catalog(["pechuga"]));
        breastQuestion.Trace.Should().ContainSingle(trace =>
            trace.OperationId == "commerce.search_products" && !trace.Skipped);

        await flow.TurnAsync("La criolla", EmptyPlan());
        flow.Cart.Items.Should().NotContain(item => item.ProductName == "PECHUGA CRIOLLA");

        await flow.TurnAsync(
            "Agrégame 2 de esa y saca el chicharrón",
            Changes(
                Add("PECHUGA CRIOLLA", 2),
                Remove("chicharrón")));
        flow.Cart.Quantity("PECHUGA CRIOLLA").Should().Be(2);
        flow.Cart.Items.Should().NotContain(item => item.ProductName == "CHICHARRON CARNUDO");

        var review = await flow.TurnAsync("Muéstrame cómo va el carrito", CartReview());
        review.Trace.Should().ContainSingle(trace =>
            trace.OperationId == "commerce.get_order_draft"
            && trace.OutcomeCode == "order.draft_loaded");
        flow.Cart.Items.Should().HaveCount(4);

        var closed = await flow.TurnAsync(
            "Ya no quiero más productos, deja por fuera lo que no encontraste",
            Facts(("order_finalized", true)));
        closed.CurrentStageId.Should().Be("order_data");

        var summary = await flow.TurnAsync(
            "Domicilio en la calle 5N, recibe Richard, teléfono 3012926660 y pago con datáfono",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 5N"),
                ("delivery_recipient_name", "Richard"),
                ("delivery_phone", "3012926660"),
                ("payment_method", "datafono")));
        AssertCheckoutAwaitingConfirmation(flow, summary, "order_checkout_card_terminal");

        var completed = await flow.TurnAsync("Sí, ese pedido está bien", Facts(("customer_confirmed", true)));
        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(4);
    }

    [Fact]
    public async Task CorrectionsAfterSummary_ReopenRequiredCheckpointAndUseOnlyTheLatestCartDeliveryAndPayment()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Agrega 2 rancheras y 1 papa ripio",
            Changes(Add("ranchera super", 2), Add("papa ripio", 1)));
        await flow.TurnAsync("Eso es todo", Facts(("order_finalized", true)));
        await flow.TurnAsync(
            "Domicilio, calle 10 # 4-20, teléfono 3007008000",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 10 # 4-20"),
                ("delivery_phone", "3007008000")));
        await flow.TurnAsync("Efectivo", Facts(("payment_method", "efectivo")));
        flow.Checkout.CallCount.Should().Be(1);

        var addressCorrection = await flow.TurnAsync(
            "Corrige la dirección: es carrera 9 # 11-30",
            Facts(("delivery_address", "Carrera 9 # 11-30")));

        addressCorrection.Facts.Should().Contain("order_checkout_presented", "true");
        addressCorrection.Facts.Should().NotContainKey("customer_confirmed");
        flow.Checkout.CallCount.Should().Be(2,
            "changing a checkout dependency must regenerate the authoritative summary");
        flow.Checkout.LastAddress.Should().Be("Carrera 9 # 11-30");
        flow.CreateOrder.CallCount.Should().Be(0);

        var paymentCorrection = await flow.TurnAsync(
            "Mejor pago con datáfono",
            Facts(("payment_method", "datafono")));
        flow.Checkout.CallCount.Should().Be(3);
        flow.Checkout.LastTemplateId.Should().Be("order_checkout_card_terminal");
        paymentCorrection.Facts.Should().NotContainKey("customer_confirmed");

        var cartCorrection = await flow.TurnAsync(
            "Agrega una ranchera más y saca la papa ripio",
            Changes(Add("ranchera super", 1), Remove("papa ripio")));
        cartCorrection.Facts.Should().NotContainKey("order_finalized");
        cartCorrection.Facts.Should().NotContainKey("order_checkout_presented");
        flow.Cart.Quantity("SALCHICHA RANCHERA SUPER X 525 GR").Should().Be(3);
        flow.Cart.Items.Should().NotContain(item => item.ProductName == "PAPA RIPIO X 1 KG");
        flow.CreateOrder.CallCount.Should().Be(0);

        var renewedSummary = await flow.TurnAsync(
            "Ahora sí, eso es todo",
            Facts(("order_finalized", true)));
        renewedSummary.Facts.Should().Contain("order_checkout_presented", "true");
        flow.Checkout.CallCount.Should().Be(4);

        var completed = await flow.TurnAsync("Sí confirmo", Facts(("customer_confirmed", true)));
        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(1);
        flow.CreateOrder.LastPaymentMethod.Should().Be("datafono");
        flow.CreateOrder.LastAddress.Should().Be("Carrera 9 # 11-30");
    }

    [Fact]
    public async Task EmptyCartFinalization_IsRejectedThenTheRecoveredOrderCanComplete()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();

        var rejected = await flow.TurnAsync(
            "Eso es todo",
            Facts(("order_finalized", true)));

        rejected.CurrentStageId.Should().Be("product_selection");
        rejected.Facts.Should().NotContainKey("order_finalized");
        flow.Checkout.CallCount.Should().Be(0);
        flow.CreateOrder.CallCount.Should().Be(0);

        await flow.TurnAsync(
            "Entonces agrega 2 jamonadas Cunichef",
            Changes(Add("jamonada cunichef", 2)));
        await flow.TurnAsync("Ahora sí, solo eso", Facts(("order_finalized", true)));
        await flow.TurnAsync(
            "Domicilio en la calle 12 # 7-40, teléfono 3002223344",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 12 # 7-40"),
                ("delivery_phone", "3002223344")));
        await flow.TurnAsync("Efectivo", Facts(("payment_method", "efectivo")));

        var completed = await flow.TurnAsync("Sí confirmo", Facts(("customer_confirmed", true)));

        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(1);
    }

    [Fact]
    public async Task StockAndAvailabilityProblems_CanBeClarifiedDiscardedAndCompletedInOneConversation()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();

        var initial = await flow.TurnAsync(
            "Agrega 1 jamonada, 5 papas escasas y 2 salchichas agotadas",
            Changes(
                Add("jamonada cunichef", 1),
                Add("papa escasa", 5),
                Add("salchicha agotada", 2)));

        initial.Trace.Should().Contain(trace =>
            trace.OperationId == "commerce.apply_order_changes"
            && trace.OutcomeCode == "cart.partially_applied");
        flow.Cart.Items.Should().ContainSingle(item =>
            item.ProductName == "JAMONADA CUNICHEF X 500 GR");
        flow.Facts["system.pending_cart_commands"].Should()
            .Contain("papa escasa")
            .And.Contain("salchicha agotada");

        var stockClarification = await flow.TurnAsync(
            "De PAPA ESCASA X 2 KG ponme solamente 2",
            Changes(Add("PAPA ESCASA X 2 KG", 2)));
        stockClarification.Trace.Should().Contain(trace =>
            trace.OperationId == "commerce.apply_order_changes");
        flow.Cart.Quantity("PAPA ESCASA X 2 KG").Should().Be(2);
        flow.Facts["system.pending_cart_commands"].Should().Contain("salchicha agotada");

        var discarded = await flow.TurnAsync(
            "Eso es todo, deja por fuera lo agotado",
            Facts(("order_finalized", true)));
        discarded.CurrentStageId.Should().Be("order_data");
        flow.Facts.Should().NotContainKey("system.pending_cart_commands");
        flow.Cart.Items.Should().HaveCount(2);

        await flow.TurnAsync(
            "Domicilio, carrera 4 # 18-10, celular 3009876543, pago en efectivo",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Carrera 4 # 18-10"),
                ("delivery_phone", "3009876543"),
                ("payment_method", "efectivo")));
        var completed = await flow.TurnAsync("Sí", Facts(("customer_confirmed", true)));

        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(2);
    }

    [Fact]
    public async Task RecoverableCheckoutFailure_ReopensSelectionAndRetriesWithoutDuplicatingTheCart()
    {
        var flow = new CjConversationHarness();
        flow.Checkout.FailNext("missing_prerequisites");
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Agrega 2 rancheras",
            Changes(Add("ranchera super", 2)));
        await flow.TurnAsync("Eso es todo", Facts(("order_finalized", true)));
        await flow.TurnAsync(
            "Domicilio en calle 20 # 1-15, teléfono 3010001100",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 20 # 1-15"),
                ("delivery_phone", "3010001100")));

        var failedCheckout = await flow.TurnAsync(
            "Efectivo",
            Facts(("payment_method", "efectivo")));

        failedCheckout.Trace.Should().Contain(trace =>
            trace.OperationId == "commerce.prepare_checkout"
            && trace.OutcomeCode == "missing_prerequisites");
        failedCheckout.Facts.Should().NotContainKey("order_finalized");
        failedCheckout.Facts.Should().NotContainKey("order_checkout_presented");
        flow.Cart.Quantity("SALCHICHA RANCHERA SUPER X 525 GR").Should().Be(2);
        flow.CreateOrder.CallCount.Should().Be(0);

        var recovered = await flow.TurnAsync(
            "Ya está bien, continúa con ese pedido",
            Facts(("order_finalized", true)));
        recovered.Facts.Should().Contain("order_checkout_presented", "true");
        flow.Checkout.CallCount.Should().Be(2);
        flow.Cart.Quantity("SALCHICHA RANCHERA SUPER X 525 GR").Should().Be(2);

        var completed = await flow.TurnAsync("Sí confirmo", Facts(("customer_confirmed", true)));
        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task CatalogQuestionsAcrossDeliveryAndPayment_DoNotLoseFactsAndALateAddReopensTheCart()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Agrega una jamonada",
            Changes(Add("jamonada cunichef", 1)));
        await flow.TurnAsync("Solo eso", Facts(("order_finalized", true)));

        var duringDelivery = await flow.TurnAsync(
            "Antes, ¿qué rancheras tienes?",
            Catalog(["ranchera"]));
        duringDelivery.CurrentStageId.Should().Be("order_data");
        duringDelivery.Facts.Should().Contain("order_finalized", "true");
        duringDelivery.Trace.Should().Contain(trace =>
            trace.OperationId == "commerce.search_products" && !trace.Skipped);

        await flow.TurnAsync(
            "Domicilio, calle 3 # 9-11, teléfono 3001002000",
            Facts(
                ("delivery_method", "domicilio"),
                ("delivery_address", "Calle 3 # 9-11"),
                ("delivery_phone", "3001002000")));

        var duringPayment = await flow.TurnAsync(
            "¿Y qué pechugas tienes?",
            Catalog(["pechuga"]));
        duringPayment.CurrentStageId.Should().Be("payment_method");
        duringPayment.Facts.Should().Contain("delivery_address", "Calle 3 # 9-11");
        duringPayment.Facts.Should().Contain("delivery_phone", "3001002000");

        var lateAdd = await flow.TurnAsync(
            "Agrega 2 pechugas criollas",
            Changes(Add("PECHUGA CRIOLLA", 2)));
        lateAdd.Facts.Should().NotContainKey("order_finalized");
        flow.Cart.Items.Should().HaveCount(2);

        await flow.TurnAsync("Ahora sí termina el pedido", Facts(("order_finalized", true)));
        await flow.TurnAsync("Datáfono", Facts(("payment_method", "datafono")));
        var completed = await flow.TurnAsync("Sí, confirmo", Facts(("customer_confirmed", true)));

        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.CreatedItemCount.Should().Be(2);
        flow.CreateOrder.LastAddress.Should().Be("Calle 3 # 9-11");
    }

    [Fact]
    public async Task PickupOrder_UsesConfiguredLocationAndStillRequiresFinalConfirmation()
    {
        var flow = new CjConversationHarness();
        await flow.ReachProductSelectionAsync();
        await flow.TurnAsync(
            "Agrega una jamonada Cunichef",
            Changes(Add("jamonada cunichef", 1)));
        await flow.TurnAsync("Eso es todo", Facts(("order_finalized", true)));

        var paymentStage = await flow.TurnAsync(
            "Voy a recogerlo en el punto de CJ; mi celular es 3005556677",
            Facts(
                ("delivery_method", "recogida"),
                ("delivery_address", "Punto de CJ Distribuciones"),
                ("delivery_phone", "3005556677")));

        paymentStage.CurrentStageId.Should().Be("payment_method");
        paymentStage.Facts.Should().Contain("delivery_method", "recogida");
        paymentStage.Facts.Should().Contain("delivery_address", "Punto de CJ Distribuciones");

        var summary = await flow.TurnAsync(
            "Pago en efectivo",
            Facts(("payment_method", "efectivo")));
        AssertCheckoutAwaitingConfirmation(flow, summary, "order_checkout_no_payment");

        var completed = await flow.TurnAsync(
            "Sí, confirmo el pedido",
            Facts(("customer_confirmed", true)));

        completed.RequestCompleted.Should().BeTrue();
        flow.CreateOrder.CallCount.Should().Be(1);
        flow.CreateOrder.LastAddress.Should().Be("Punto de CJ Distribuciones");
    }

    private static void AssertCheckoutAwaitingConfirmation(
        CjConversationHarness flow,
        DeterministicTurnResult result,
        string templateId)
    {
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.CurrentStageId.Should().Be("order_confirmation");
        result.Facts.Should().Contain("order_checkout_presented", "true");
        result.Facts.Should().NotContainKey("customer_confirmed");
        result.Presentations.Should().Contain(presentation => presentation.TemplateId == templateId);
        result.RequestCompleted.Should().BeFalse();
        flow.CreateOrder.CallCount.Should().Be(0);
    }

    private static TurnPlan EmptyPlan() => Plan();

    private static TurnPlan Facts(params (string Key, object Value)[] values) =>
        Plan(facts: values.Select(value => new PlannedFactClaim
        {
            Key = value.Key,
            Value = JsonSerializer.SerializeToElement(value.Value),
            Evidence = value.Value.ToString() ?? value.Key,
            Confidence = 1
        }).ToArray());

    private static TurnPlan Changes(params CartCommand[] commands) =>
        Plan(signals:
        [
            Signal("order_changes", commands.Select(command => new
            {
                operation = command.Operation,
                productText = command.ProductText,
                quantity = command.Quantity,
                destinationReference = command.DestinationReference
            }).ToArray())
        ]);

    private static TurnPlan Catalog(IReadOnlyList<string> queries, string? replacementReference = null) =>
        Plan(signals:
        [
            Signal("catalog_query", new
            {
                queries,
                replacement_reference = replacementReference
            })
        ]);

    private static TurnPlan Recipe(string ingredient) =>
        Plan(signals: [Signal("recipe_request", ingredient)]);

    private static TurnPlan CartReview() =>
        Plan(signals: [Signal("cart_review_request", new { })]);

    private static TurnPlan Plan(
        IReadOnlyList<PlannedFactClaim>? facts = null,
        IReadOnlyList<PlannedSignal>? signals = null) =>
        new()
        {
            FlowIntent = new PlannedFlowIntent
            {
                CandidateFlow = "order",
                Confidence = 1,
                Evidence = "scripted CJ regression"
            },
            Facts = facts ?? [],
            Signals = signals ?? []
        };

    private static PlannedSignal Signal(string type, object value) => new()
    {
        Type = type,
        Value = JsonSerializer.SerializeToElement(value),
        Evidence = type,
        Confidence = 1
    };

    private static CartCommand Add(string product, decimal quantity) =>
        new(CartCommandOperations.Add, product, quantity, null);

    private static CartCommand SetQuantity(string product, decimal quantity) =>
        new(CartCommandOperations.SetQuantity, product, quantity, null);

    private static CartCommand Remove(string product) =>
        new(CartCommandOperations.Remove, product, null, null);

    private sealed class CjConversationHarness
    {
        private readonly ScriptedPlanner _planner = new();
        private readonly InMemoryFactsService _factStore = new();
        private readonly ConversationState _state = new()
        {
            ActiveFlowId = "order",
            ActiveStageId = "customer_name",
            ActiveRequestStartedAtUtc = DateTime.UtcNow
        };
        private readonly HashSet<string> _executedActionKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly AgentConversationContext _session;
        private readonly DeterministicTurnCoordinator _coordinator;

        public CjConversationHarness()
        {
            Config = LoadCjConfig();
            Resolver = new ScenarioProductResolver(CreateProducts());
            Cart = new StatefulCartStore();
            _planner.Cart = Cart;
            var cartOperation = new ApplyOrderChangesOperation(
                new CartCommandBatchProcessor(Resolver, Cart),
                _factStore);
            Catalog = new ScenarioCatalogOperation(_factStore, Resolver);
            Checkout = new ObservableCheckoutOperation(Cart);
            CreateOrder = new ObservableCreateOrderOperation(Cart);
            Draft = new ObservableDraftOperation(Cart);
            var recipes = new ScenarioRecipeOperation();

            _coordinator = new DeterministicTurnCoordinator(
                _planner,
                new DeterministicFlowSelector(),
                new FactMutationBatchProcessor(),
                _factStore,
                new ConversationVerificationService(),
                new DeterministicStageExecutor(
                    new AgentOperationRegistry(
                        [cartOperation, Catalog, Draft, Checkout, CreateOrder, recipes]),
                    new StageConditionEvaluator(),
                    new OperationArgumentBinder()),
                new DeterministicStageTransitionResolver(new StageConditionEvaluator()));

            _session = new AgentConversationContext
            {
                AgentId = Config.AgentId,
                BusinessId = Config.BusinessId,
                ConversationId = Guid.NewGuid(),
                BusinessToday = new DateOnly(2026, 7, 17),
                BusinessNow = DateTimeOffset.Parse("2026-07-17T10:00:00-05:00"),
                Config = Config,
                ConversationState = _state
            };

            foreach (var fact in Config.FactSchema.Where(fact =>
                         !string.IsNullOrWhiteSpace(fact.DefaultValue)))
            {
                Facts[fact.Key] = fact.DefaultValue!;
                FactVersions[fact.Key] = 1;
            }
            Facts.TryAdd("city", "Valledupar");
            FactVersions.TryAdd("city", 1);
        }

        public AgentConfig Config { get; }
        public ScenarioProductResolver Resolver { get; }
        public StatefulCartStore Cart { get; }
        public ScenarioCatalogOperation Catalog { get; }
        public ObservableDraftOperation Draft { get; }
        public ObservableCheckoutOperation Checkout { get; }
        public ObservableCreateOrderOperation CreateOrder { get; }
        public Dictionary<string, string> Facts { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> FactVersions { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task ReachProductSelectionAsync()
        {
            await TurnAsync("Richard", CjCompleteOrderFlowRegressionTests.Facts(("customer_name", "Richard")));
            var profile = await TurnAsync(
                "Restaurante",
                CjCompleteOrderFlowRegressionTests.Facts(("customer_type", "Restaurante")));
            profile.CurrentStageId.Should().Be("product_selection");
        }

        public async Task<DeterministicTurnResult> TurnAsync(string message, TurnPlan plan)
        {
            _planner.Next = plan;
            _session.LatestUserMessage = message;
            SyncSessionFacts();
            var flow = AgentFlowCatalog.Find(Config, "order")!;
            var currentStage = DeterministicConversationPosition.ResolveStage(
                flow,
                _state,
                Facts,
                Config.FactSchema);
            _state.ActiveStageId = currentStage.Id;

            var result = await _coordinator.ExecuteAsync(new DeterministicTurnRequest
            {
                Config = Config,
                OperationContext = new OperationContext
                {
                    AgentId = Config.AgentId,
                    BusinessId = Config.BusinessId,
                    ConversationId = _session.ConversationId,
                    BusinessToday = _session.BusinessToday,
                    BusinessNow = _session.BusinessNow,
                    Config = Config,
                    ConversationState = _state,
                    Facts = Facts,
                    Session = _session
                },
                CurrentFacts = Facts,
                FactVersions = FactVersions,
                ExecutedActionKeys = _executedActionKeys,
                CurrentFlowId = "order",
                ActiveFlowId = "order",
                CurrentStageId = currentStage.Id,
                HasOpenPrimaryRequest = true,
                LatestUserMessage = message
            });

            result.Success.Should().BeTrue(string.Join("; ", result.Errors));
            Facts = new Dictionary<string, string>(result.Facts, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in _factStore.Snapshot(_session.ConversationId))
            {
                if (value is null)
                    Facts.Remove(key);
                else
                    Facts[key] = value;
            }
            foreach (var fact in Config.FactSchema.Where(fact =>
                         !string.IsNullOrWhiteSpace(fact.DefaultValue)))
            {
                Facts.TryAdd(fact.Key, fact.DefaultValue!);
            }
            Facts.TryAdd("city", "Valledupar");
            FactVersions = new Dictionary<string, long>(
                result.FactVersions,
                StringComparer.OrdinalIgnoreCase);
            _state.ActiveStageId = result.CurrentStageId;
            _state.LastUserMessage = message;
            SyncSessionFacts();
            return result;
        }

        private void SyncSessionFacts()
        {
            _session.Facts.Clear();
            foreach (var (key, value) in Facts)
                _session.Facts[key] = value;
        }
    }

    private sealed class ScriptedPlanner : ITurnPlanner
    {
        public StatefulCartStore? Cart { get; set; }
        public TurnPlan Next { get; set; } = EmptyPlan();

        public Task<TurnPlanProposal> PlanAsync(
            TurnPlanningContext context,
            CancellationToken ct = default)
        {
            var structured = new Dictionary<string, JsonElement>(
                context.StructuredContext
                    ?? new Dictionary<string, JsonElement>(),
                StringComparer.OrdinalIgnoreCase);
            if (Cart is { Items.Count: > 0 })
            {
                structured["currentCart"] = JsonSerializer.SerializeToElement(new
                {
                    items = Cart.Items.Select(item => new { name = item.ProductName, item.Quantity })
                });
            }
            var normalized = CommerceTurnPlanSafety.Normalize(
                Next,
                context with { StructuredContext = structured });
            return Task.FromResult(new TurnPlanProposal(true, normalized, [], 0, 0));
        }
    }

    private sealed class ScenarioProductResolver : ICartProductResolver
    {
        private readonly Dictionary<string, IReadOnlyList<ProductReference>> _products =
            new(StringComparer.OrdinalIgnoreCase);

        public ScenarioProductResolver(IReadOnlyDictionary<string, ProductReference> products)
        {
            foreach (var (alias, product) in products)
                AddAlias(alias, product);
            foreach (var product in products.Values.DistinctBy(ProductKey))
            {
                AddAlias(product.Name, product);
                if (!string.IsNullOrWhiteSpace(product.Sku))
                    AddAlias(product.Sku, product);
            }

            AddAlias("tocineta", products["tocineta nojos"]);
            AddAlias("tocineta", products["tocineta el coleo"]);
        }

        public IReadOnlyList<ProductReference> Search(string query)
        {
            if (query.Contains("maíz", StringComparison.OrdinalIgnoreCase)
                || query.Contains("maiz", StringComparison.OrdinalIgnoreCase))
            {
                return Unique(
                    _products["maíz tierno"]
                        .Concat(_products["maíz super dulce"]));
            }
            if (query.Contains("pechuga", StringComparison.OrdinalIgnoreCase))
            {
                return Unique(
                    _products["trozos de pechuga"]
                        .Concat(_products["pechuga mac pollo"])
                        .Concat(_products["pechuga criolla"]));
            }
            if (query.Contains("cerdo", StringComparison.OrdinalIgnoreCase))
                return _products["pierna de cerdo tajada"];
            if (query.Contains("tocineta", StringComparison.OrdinalIgnoreCase))
                return _products["tocineta"];
            if (query.Contains("salsa", StringComparison.OrdinalIgnoreCase))
                return _products["salsa de ajo"];
            return _products.TryGetValue(query, out var exact) ? exact : [];
        }

        public Task<IReadOnlyList<ProductReference>> FindAsync(
            AgentConversationContext context,
            string productText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_products.TryGetValue(productText, out var products)
                ? products
                : (IReadOnlyList<ProductReference>)[]);

        public Task<ProductResolution> ResolveAsync(
            AgentConversationContext context,
            string productText,
            CancellationToken cancellationToken = default)
        {
            if (!_products.TryGetValue(productText, out var products) || products.Count == 0)
                return Task.FromResult(ProductResolution.NotFound(productText));

            var candidates = products
                .Select(product => new ProductResolutionCandidate(
                    product,
                    1d,
                    ProductMatchSource.Catalog))
                .ToList();
            if (products.Count > 1)
            {
                return Task.FromResult(new ProductResolution(
                    ProductResolutionStatus.Ambiguous,
                    null,
                    candidates,
                    productText));
            }

            var selected = products[0];
            return Task.FromResult(new ProductResolution(
                selected.IsActive
                    ? ProductResolutionStatus.Resolved
                    : ProductResolutionStatus.Unavailable,
                selected.IsActive ? selected : null,
                candidates,
                productText));
        }

        private void AddAlias(string alias, ProductReference product)
        {
            if (!_products.TryGetValue(alias, out var existing))
            {
                _products[alias] = [product];
                return;
            }
            _products[alias] = Unique(existing.Append(product));
        }

        private static IReadOnlyList<ProductReference> Unique(IEnumerable<ProductReference> products) =>
            products.DistinctBy(ProductKey).ToList();

        private static string ProductKey(ProductReference product) =>
            product.ExternalProductId ?? product.Sku ?? product.Name;
    }

    private sealed class StatefulCartStore : ICartMutationStore
    {
        private readonly List<OrderItemSnapshot> _items = [];
        public IReadOnlyList<OrderItemSnapshot> Items => _items;

        public decimal Quantity(string productName) =>
            _items.Should().ContainSingle(item => item.ProductName == productName).Subject.Quantity;

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
                        ProductIdentity(item, product));
                    if (existing is null)
                    {
                        _items.Add(new OrderItemSnapshot(
                            Guid.NewGuid(),
                            product.ProductId,
                            product.ExternalProductId,
                            product.Sku,
                            product.Name,
                            command.Quantity!.Value,
                            product.UnitPrice,
                            product.UnitPrice * command.Quantity.Value));
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

        public OrderSnapshot Snapshot()
        {
            var total = _items.Sum(item => item.LineTotal);
            return new OrderSnapshot(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                total,
                0,
                0,
                total,
                _items.ToList());
        }

        private static bool ProductIdentity(OrderItemSnapshot item, ProductReference product) =>
            !string.IsNullOrWhiteSpace(product.ExternalProductId)
                && item.ExternalProductId == product.ExternalProductId
            || !string.IsNullOrWhiteSpace(product.Sku)
                && item.Sku == product.Sku;

        private void Replace(OrderItemSnapshot item, decimal quantity)
        {
            var index = _items.IndexOf(item);
            _items[index] = item with
            {
                Quantity = quantity,
                LineTotal = item.UnitPrice * quantity
            };
        }
    }

    private sealed class ScenarioCatalogOperation(
        InMemoryFactsService factStore,
        ScenarioProductResolver resolver) : IAgentOperation
    {
        public int CallCount { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "commerce.search_products",
            "{\"type\":\"object\",\"required\":[]}",
            ["products.found", "products.not_found"],
            [],
            [],
            []);

        public async Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var queries = ReadQueries(input);
            var products = queries.SelectMany(resolver.Search)
                .DistinctBy(product => product.ExternalProductId ?? product.Sku ?? product.Name)
                .ToList();
            if (products.Count == 0)
                return OperationOutcome.Ok("products.not_found", new { queries, products });

            var replacement = input.TryGetProperty("replacement_reference", out var replacementElement)
                && replacementElement.ValueKind == JsonValueKind.String
                ? replacementElement.GetString()
                : null;
            if (context.Session is not null)
            {
                await CatalogOfferMemory.RememberAsync(
                    factStore,
                    context.Session,
                    products,
                    queries,
                    cancellationToken,
                    replacement);
            }
            context.ConversationState.LastBotMessage = string.Join(
                '\n',
                products.Select(product => product.Name));

            return OperationOutcome.Ok(
                "products.found",
                new
                {
                    queries,
                    products = products.Select(product => new
                    {
                        external_product_id = product.ExternalProductId,
                        product.Name,
                        unit_price = product.UnitPrice,
                        product.Currency
                    })
                });
        }

        private static IReadOnlyList<string> ReadQueries(JsonElement input)
        {
            if (!input.TryGetProperty("queries", out var queries))
                return [];
            if (queries.ValueKind == JsonValueKind.Array)
                return queries.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToList();
            if (queries.ValueKind != JsonValueKind.String)
                return [];

            var raw = queries.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return [];
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return document.RootElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToList();
                }
            }
            catch (JsonException)
            {
                // The binder may legitimately supply a single plain-text query.
            }
            return [raw];
        }
    }

    private sealed class ScenarioRecipeOperation : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "commerce.search_recipes",
            "{\"type\":\"object\",\"required\":[]}",
            ["recipes.found"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok(
                "recipes.found",
                new
                {
                    recipes = new[]
                    {
                        new { title = "Pechuga con tocineta", url = "https://example.test/recipe" }
                    },
                    catalog_search_queries = new[] { "pechuga", "tocineta", "salsa" }
                }));
    }

    private sealed class ObservableDraftOperation(StatefulCartStore cart) : IAgentOperation
    {
        public int CallCount { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "commerce.get_order_draft",
            "{\"type\":\"object\",\"required\":[]}",
            ["order.draft_loaded", "order.draft_empty", "order_draft_missing"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var snapshot = cart.Snapshot();
            return Task.FromResult(snapshot.Items.Count == 0
                ? OperationOutcome.Ok("order.draft_empty", new { order = snapshot })
                : OperationOutcome.Ok("order.draft_loaded", new { order = snapshot }));
        }
    }

    private sealed class ObservableCheckoutOperation(StatefulCartStore cart) : IAgentOperation
    {
        private readonly Queue<string> _nextOutcomes = [];
        public int CallCount { get; private set; }
        public string? LastTemplateId { get; private set; }
        public string? LastOutcomeCode { get; private set; }
        public string? LastAddress { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "commerce.prepare_checkout",
            "{\"type\":\"object\",\"required\":[]}",
            [
                "order.checkout_ready",
                "order.checkout_payment_required",
                "order.checkout_pending_manual_payment",
                "order_draft_missing",
                "missing_prerequisites"
            ],
            [],
            ["order_checkout_no_payment", "order_checkout_card_terminal", "order_checkout_manual_payment"],
            []);

        public void FailNext(string outcomeCode) => _nextOutcomes.Enqueue(outcomeCode);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            context.Facts.TryGetValue("payment_method", out var paymentMethod);
            context.Facts.TryGetValue("delivery_address", out var address);
            LastAddress = address;
            if (_nextOutcomes.TryDequeue(out var forcedOutcome))
            {
                LastOutcomeCode = forcedOutcome;
                LastTemplateId = null;
                return Task.FromResult(OperationOutcome.Fail(
                    forcedOutcome,
                    "Forced recoverable checkout regression failure.",
                    recoverable: true));
            }
            LastOutcomeCode = paymentMethod == "transferencia"
                ? "order.checkout_pending_manual_payment"
                : "order.checkout_ready";
            LastTemplateId = paymentMethod switch
            {
                "datafono" => "order_checkout_card_terminal",
                "transferencia" => "order_checkout_manual_payment",
                _ => "order_checkout_no_payment"
            };
            var snapshot = cart.Snapshot();
            return Task.FromResult(OperationOutcome.Ok(
                LastOutcomeCode,
                new { order = snapshot },
                [
                    new OperationPresentation(
                        LastTemplateId,
                        new Dictionary<string, object?>
                        {
                            ["delivery_address"] = address,
                            ["payment_method"] = paymentMethod,
                            ["total"] = snapshot.Total
                        },
                        FragmentRenderMode.Exclusive,
                        FragmentPriority.Required)
                ]));
        }
    }

    private sealed class ObservableCreateOrderOperation(StatefulCartStore cart) : IAgentOperation
    {
        public int CallCount { get; private set; }
        public int CreatedItemCount { get; private set; }
        public string? LastPaymentMethod { get; private set; }
        public string? LastAddress { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "commerce.create_order",
            "{\"type\":\"object\",\"required\":[\"customer_confirmed\"]}",
            ["order.created"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            input.GetProperty("customer_confirmed").ValueKind.Should()
                .BeOneOf(JsonValueKind.True, JsonValueKind.String);
            context.Facts["customer_confirmed"].Should().Be("true");
            CallCount++;
            CreatedItemCount = cart.Items.Count;
            context.Facts.TryGetValue("payment_method", out var paymentMethod);
            context.Facts.TryGetValue("delivery_address", out var address);
            LastPaymentMethod = paymentMethod;
            LastAddress = address;
            return Task.FromResult(OperationOutcome.Ok(
                "order.created",
                new { order_id = $"CJ-{CallCount}" }));
        }
    }

    private sealed class InMemoryFactsService : IConversationFactsService
    {
        private readonly Dictionary<Guid, Dictionary<string, string?>> _facts = [];

        public IReadOnlyDictionary<string, string?> Snapshot(Guid conversationId) =>
            _facts.TryGetValue(conversationId, out var facts)
                ? new Dictionary<string, string?>(facts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public Task ApplyBatchAsync(
            Guid conversationId,
            Guid businessId,
            IReadOnlyDictionary<string, string?> mutations,
            IReadOnlySet<string> rememberAcrossRequests,
            CancellationToken ct = default)
        {
            var facts = Get(conversationId);
            foreach (var (key, value) in mutations)
                facts[key] = value;
            return Task.CompletedTask;
        }

        public Task SetAsync(
            Guid conversationId,
            Guid businessId,
            string key,
            string value,
            bool rememberAcrossRequests = false,
            CancellationToken ct = default)
        {
            Get(conversationId)[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(
            Guid conversationId,
            string key,
            CancellationToken ct = default)
        {
            var facts = Get(conversationId);
            return Task.FromResult(facts.TryGetValue(key, out var value) ? value : null);
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            Guid conversationId,
            CancellationToken ct = default)
        {
            var values = Get(conversationId)
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(values);
        }

        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(
            Guid conversationId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationFactRecord>>([]);

        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(
            Guid conversationId,
            IReadOnlyCollection<string> persistentKeys,
            CancellationToken ct = default) =>
            ClearFieldsAsync(
                conversationId,
                Get(conversationId).Keys.Except(persistentKeys, StringComparer.OrdinalIgnoreCase).ToList(),
                ct);

        public Task<IReadOnlyList<string>> ClearFieldsAsync(
            Guid conversationId,
            IReadOnlyCollection<string> fields,
            CancellationToken ct = default)
        {
            var facts = Get(conversationId);
            var cleared = new List<string>();
            foreach (var field in fields)
            {
                if (!facts.ContainsKey(field))
                    continue;
                facts[field] = null;
                cleared.Add(field);
            }
            return Task.FromResult<IReadOnlyList<string>>(cleared);
        }

        private Dictionary<string, string?> Get(Guid conversationId)
        {
            if (!_facts.TryGetValue(conversationId, out var facts))
            {
                facts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                _facts[conversationId] = facts;
            }
            return facts;
        }
    }

    private static IReadOnlyDictionary<string, ProductReference> CreateProducts() =>
        new Dictionary<string, ProductReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["trozos de pechuga"] = Product("TROZOS DE PECHUGA DE POLLO", "PE1", 14_798.55m),
            ["pechuga mac pollo"] = Product("PECHUGA MAC POLLO", "PE2", 13_001.08m),
            ["pechuga criolla"] = Product("PECHUGA CRIOLLA", "PE3", 14_033.67m),
            ["tocineta nojos"] = Product("TOCINETA NOJOS X 1000GR", "TO1", 19_722.04m),
            ["tocineta el coleo"] = Product("TOCINETA EL COLEO X 500 GR", "TO2", 10_825.38m),
            ["maíz tierno"] = Product("MAIZ TIERNO CONGELADO", "MA1", 8_000m),
            ["maíz super dulce"] = Product("MAIZ SUPER DULCE X 500 GR", "MA2", 9_000m),
            ["chicharrón"] = Product("CHICHARRON CARNUDO", "CH1", 12_000m),
            ["ranchera super"] = Product("SALCHICHA RANCHERA SUPER X 525 GR", "RA1", 11_000m),
            ["papa farm frites"] = Product("PAPA FARM FRITES X 2.5 KG", "PA1", 21_800m),
            ["papa ripio"] = Product("PAPA RIPIO X 1 KG", "PA2", 7_500m),
            ["jamonada cunichef"] = Product("JAMONADA CUNICHEF X 500 GR", "JA1", 9_500m),
            ["pierna de cerdo tajada"] = Product("PIERNA DE CERDO TAJADA", "CE1", 12_456.33m),
            ["salsa de ajo"] = Product("SALSA DE AJO X 200 GR", "SA1", 7_190.28m),
            ["papa escasa"] = Product("PAPA ESCASA X 2 KG", "ST1", 18_000m, 2m),
            ["salchicha agotada"] = Product("SALCHICHA AGOTADA X 500 GR", "ST2", 10_000m, 0m, false)
        };

    private static ProductReference Product(
        string name,
        string sku,
        decimal price,
        decimal stock = 100m,
        bool active = true) =>
        new ProductReference(null, sku, sku, name, null, null, price, "COP", stock)
        {
            IsActive = active
        };

    private static AgentConfig LoadCjConfig()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedCJDistribuciones.sql");
        var sql = File.ReadAllText(path);
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the CJ seed must declare @SettingsJson");
        var settingsJson = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        var root = JsonNode.Parse(settingsJson)!.AsObject();
        var globalActions = root["globalActions"]!.AsArray();
        var appendedGlobalActions = Regex.Matches(
            sql,
            "JSON_MODIFY\\s*\\(\\s*@SettingsJson\\s*,\\s*'append\\s+\\$\\.globalActions'\\s*,\\s*JSON_QUERY\\s*\\(\\s*N'(?<json>.*?)'\\s*\\)\\s*\\)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match appended in appendedGlobalActions)
        {
            var appendedJson = appended.Groups["json"].Value.Replace(
                "''",
                "'",
                StringComparison.Ordinal);
            globalActions.Add(JsonNode.Parse(appendedJson));
        }
        appendedGlobalActions.Count.Should().BeGreaterThan(0,
            "the CJ seed appends cross-stage actions after declaring its base JSON");
        settingsJson = root.ToJsonString();

        var config = JsonSerializer.Deserialize<AgentConfig>(settingsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        });
        config.Should().NotBeNull();
        return config!;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }
}
