using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Consulta el estado del anticipo en el proveedor de pagos.
/// No crea reservas ni confirma pagos; eso lo hace el webhook de Wompi via PaymentConfirmationHandler.
/// </summary>
[AgentToolMetadata("verify_payment")]
public sealed class VerifyPaymentTool : IAgentTool
{
private readonly IPaymentLinkService _paymentLinks;
    private readonly IPaymentLifecycleService _paymentLifecycle;

    public VerifyPaymentTool(
        IPaymentLinkService paymentLinks,
        IPaymentLifecycleService paymentLifecycle)
    {
        _paymentLinks = paymentLinks;
        _paymentLifecycle = paymentLifecycle;
    }

    public string Name => "verify_payment";

    public string Description =>
        "Read-only lookup of PaymentTransaction status for the current conversation. " +
        "Does not create reservations or mutate payment state.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "payment_reference_id": { "type": "string" }
          },
          "required": []
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "payment_reference_id", out var referenceId);

        var payment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetLatestByConversationAsync(ctx.ConversationId, cancellationToken);

        referenceId ??= payment?.PaymentReferenceId;

        if (string.IsNullOrWhiteSpace(referenceId))
            return ToolResultHelper.Error("no_payment_reference", "No payment link has been generated yet.");

        if (payment is null || !string.Equals(payment.PaymentReferenceId, referenceId, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Error(
                "payment_not_found",
                "No matching payment transaction was found for this conversation.");
        }

        if (payment.Status == PaymentTransactionStatus.Confirmed)
        {
            return ToolResultHelper.Ok(new
            {
                status = "confirmed",
                is_approved = true,
                payment_transaction_id = payment.PaymentTransactionId,
                reservation_id = payment.ReservationId,
                requires_webhook_fulfillment = payment.ReservationId is null,
                next_action = payment.ReservationId is null
                    ? "await_webhook_fulfillment_or_escalate_operations"
                    : "payment_already_confirmed",
                message = payment.ReservationId is null
                    ? "Payment is confirmed but no reservation is linked yet."
                    : "Payment is already confirmed and linked to a reservation."
            });
        }

        var result = await _paymentLinks.CheckPaymentStatusAsync(referenceId, ctx.BusinessId, cancellationToken);

        return ToolResultHelper.Ok(new
        {
            status = result.IsApproved ? "approved_pending_webhook" : "pending",
            is_approved = result.IsApproved,
            transaction_id = result.TransactionId,
            amount_cents = result.AmountInCents,
            next_action = result.IsApproved
                ? "await_webhook_fulfillment"
                : "await_payment_confirmation",
            message = result.IsApproved
                ? "Payment was approved by the provider and is pending webhook fulfillment."
                : "Payment is pending provider confirmation."
        });
    }
}
