using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations.Reservation;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Compatibility adapter for the pre-deterministic runtime. Checkout business behavior lives in
/// <see cref="IReservationCheckoutPreparationService"/> and is shared with the typed operation.
/// </summary>
[AgentToolMetadata("prepare_checkout", Capabilities = new[] { ToolCapabilities.CheckoutPrepare })]
public sealed class PrepareCheckoutTool : IAgentTool
{
    private readonly IReservationCheckoutPreparationService _checkout;
    private readonly IConversationFactsService _factsService;
    private readonly IConversationVerificationService _verifications;

    public PrepareCheckoutTool(
        IReservationCheckoutPreparationService checkout,
        IConversationFactsService factsService,
        IConversationVerificationService verifications)
    {
        _checkout = checkout;
        _factsService = factsService;
        _verifications = verifications;
    }

    public string Name => "prepare_checkout";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.CheckoutPrepare];

    public string Description =>
        "Prepares an authoritative reservation or enrollment checkout from current facts and tenant configuration.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string" },
            "add_ons": { "type": "string" },
            "payment_method": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (ctx.Turn is null || ctx.Config is null)
            return ToolResultHelper.Error("internal_error", "Turn context is not available for checkout preparation.");

        var result = await _checkout.PrepareAsync(
            new ReservationCheckoutPreparationRequest(
                ctx.BusinessId,
                ctx.ConversationId,
                ctx.Config,
                ctx.ConversationState,
                ctx.Facts,
                ReadString(arguments, "service"),
                arguments.TryGetProperty("add_ons", out _),
                ReadString(arguments, "add_ons"),
                ReadString(arguments, "payment_method"),
                ctx.ActivePayment),
            cancellationToken);

        if (!result.Success || result.Quote is null)
            return Failure(result);

        var roles = new FactRoleIndex(ctx.Config.FactSchema);
        await CheckoutPaymentFact.PersistSelectionAsync(
            _factsService,
            ctx,
            roles,
            result.Quote,
            cancellationToken);

        if (result.Payment is not null)
            ctx.ActivePayment = result.Payment;
        else if (result.ActivePaymentDiscarded)
            ctx.ActivePayment = null;

        var token = ctx.Turn.RegisterFragment(
            "CHECKOUT",
            result.Quote.TemplateId,
            result.TemplateData,
            FragmentRenderMode.Exclusive);
        ctx.Turn.MarkCheckoutPrepared();

        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutPrepared,
            result.VerificationDependencies,
            ttl: null);
        if (result.Quote.CheckoutKind == CheckoutKind.Reservation
            && result.Quote.PayableCents <= 0)
        {
            _verifications.Record(
                ctx,
                VerificationFactTypes.CheckoutNoPaymentPrepared,
                result.VerificationDependencies,
                ttl: null);
        }

        return ToolResultHelper.Ok(new
        {
            checkout_token = token,
            checkout_kind = result.Quote.CheckoutKind.ToString(),
            template_id = result.Quote.TemplateId,
            payment_required = result.Quote.PayableCents > 0,
            payment_pending_manual_confirmation = result.Quote.RequiresManualConfirmation,
            payment_transaction_id = result.Payment?.PaymentTransactionId,
            is_booking_confirmed = false
        });
    }

    private static string Failure(ReservationCheckoutPreparationResult result)
    {
        if (result.MissingPrerequisites.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. result.MissingPrerequisites]);

        if (result.AvailablePaymentMethods.Count > 0)
        {
            return ToolResultHelper.ErrorWithLlm(
                result.Code,
                result.Message ?? "Checkout could not be prepared.",
                new
                {
                    next_action = "select_payment_method",
                    available_payment_methods = result.AvailablePaymentMethods
                },
                result.Recoverable);
        }

        return ToolResultHelper.Error(
            result.Code,
            result.Message ?? "Checkout could not be prepared.",
            result.Recoverable);
    }

    private static string? ReadString(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
