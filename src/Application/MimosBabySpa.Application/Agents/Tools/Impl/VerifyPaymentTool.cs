using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Verifica el estado de un pago cuando el cliente dice "ya pagué".
/// Consulta directamente al proveedor (Wompi) — no confía en el mensaje del cliente.
/// </summary>
public sealed class VerifyPaymentTool : IAgentTool
{
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IConversationStateManager _stateManager;

    public VerifyPaymentTool(IPaymentLinkService paymentLinks, IConversationStateManager stateManager)
    {
        _paymentLinks = paymentLinks;
        _stateManager = stateManager;
    }

    public string Name => "verify_payment";

    public string Description =>
        "Checks whether the customer has completed their advance payment. " +
        "Call this when the customer claims they have paid. Never confirm payment without calling this tool.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "payment_reference_id": {
              "type": "string",
              "description": "Payment reference ID from the previously generated payment link"
            }
          },
          "required": ["payment_reference_id"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        // Intentar leer del argumento; si no viene, usar el del estado de la conversación
        ToolResultHelper.TryGetString(arguments, "payment_reference_id", out var referenceId);

        if (string.IsNullOrWhiteSpace(referenceId))
        {
            var state = await _stateManager.GetOrCreateStateAsync(
                ctx.ConversationId, ctx.BusinessId, ctx.CustomerPhone, cancellationToken);
            referenceId = state.PaymentReferenceId;
        }

        if (string.IsNullOrWhiteSpace(referenceId))
            return ToolResultHelper.Error("no_payment_reference",
                "No payment link has been generated for this conversation yet.",
                "Call generate_payment_link first.");

        var result = await _paymentLinks.CheckPaymentStatusAsync(referenceId, ctx.BusinessId, cancellationToken);

        if (result.IsApproved)
        {
            // Actualizar estado para que el sistema de pagos también lo refleje
            var state = await _stateManager.GetOrCreateStateAsync(
                ctx.ConversationId, ctx.BusinessId, ctx.CustomerPhone, cancellationToken);
            state.PaymentConfirmed = true;
            await _stateManager.SaveStateAsync(ctx.ConversationId, state, cancellationToken);
        }

        return ToolResultHelper.Ok(new
        {
            is_approved = result.IsApproved,
            transaction_id = result.TransactionId,
            amount_cents = result.AmountInCents
        });
    }
}
