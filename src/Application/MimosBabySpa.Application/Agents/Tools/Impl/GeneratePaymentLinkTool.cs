using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Genera un link de pago de anticipo (Wompi).
/// Idempotente: si ya existe un link vigente para la conversación, lo reutiliza.
/// Pre-condición: debe existir una reserva creada en la sesión.
/// </summary>
public sealed class GeneratePaymentLinkTool : IAgentTool
{
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IConversationStateManager _stateManager;

    public GeneratePaymentLinkTool(IPaymentLinkService paymentLinks, IConversationStateManager stateManager)
    {
        _paymentLinks = paymentLinks;
        _stateManager = stateManager;
    }

    public string Name => "generate_payment_link";

    public string Description =>
        "Generates an advance payment link for the customer. " +
        "Call resolve_pricing first to get the amount. " +
        "Reuses an existing active link if one was already generated for this conversation.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service_description": { "type": "string", "description": "Human-readable description for the payment (e.g. 'Masaje Prenatal + Reflexología')" },
            "amount_cents": { "type": "integer", "description": "Amount in cents (from resolve_pricing)" },
            "customer_phone": { "type": "string", "description": "Customer phone number" },
            "currency": { "type": "string", "description": "Currency code, default 'COP'", "default": "COP" }
          },
          "required": ["service_description", "amount_cents", "customer_phone"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service_description", out var description))
            return ToolResultHelper.Error("invalid_args", "'service_description' is required.");

        if (!arguments.TryGetProperty("amount_cents", out var amountEl) ||
            !amountEl.TryGetInt64(out var amountCents) || amountCents <= 0)
            return ToolResultHelper.Error("invalid_args", "'amount_cents' must be a positive integer.");

        if (!ToolResultHelper.TryGetString(arguments, "customer_phone", out var phone))
            return ToolResultHelper.Error("invalid_args", "'customer_phone' is required.");

        ToolResultHelper.TryGetString(arguments, "currency", out var currency);
        if (string.IsNullOrWhiteSpace(currency)) currency = "COP";

        // Idempotencia: reutilizar link vigente si existe
        var state = await _stateManager.GetOrCreateStateAsync(
            ctx.ConversationId, ctx.BusinessId, phone, cancellationToken);

        if (!string.IsNullOrWhiteSpace(state.PaymentLinkUrl) &&
            state.PaymentLinkExpiresAt.HasValue &&
            state.PaymentLinkExpiresAt.Value > DateTime.UtcNow)
        {
            return ToolResultHelper.Ok(new
            {
                reused = true,
                payment_link_url = state.PaymentLinkUrl,
                payment_reference_id = state.PaymentReferenceId,
                expires_at = state.PaymentLinkExpiresAt
            });
        }

        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId, ctx.ConversationId, phone,
                description, amountCents, currency, ExpirationMinutes: 60),
            cancellationToken);

        if (!result.Success)
            return ToolResultHelper.Error("payment_link_failed",
                result.ErrorMessage ?? "Failed to generate payment link.",
                "Try again or escalate to a human agent.");

        // Persistir en estado para idempotencia futura
        state.PaymentLinkUrl = result.PaymentLinkUrl;
        state.PaymentReferenceId = result.PaymentReferenceId;
        state.AnticipoAmountInCents = amountCents;
        state.PaymentLinkExpiresAt = result.ExpiresAt;
        await _stateManager.SaveStateAsync(ctx.ConversationId, state, cancellationToken);

        return ToolResultHelper.Ok(new
        {
            payment_link_url = result.PaymentLinkUrl,
            payment_reference_id = result.PaymentReferenceId,
            expires_at = result.ExpiresAt
        });
    }
}
