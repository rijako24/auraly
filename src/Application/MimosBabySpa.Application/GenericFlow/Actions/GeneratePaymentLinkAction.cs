using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Generates a payment link for the current reservation.
///
/// Responsibility: generate link only. Display formatting lives in ResolvePricingAction.
///
/// Input keys (from input_mapping):
///   item — service description / name
///
/// Input keys (from node config — passed as raw JSON by ActionNodeHandler):
///   payment — JSON object with payment configuration:
///     requiresAnticipo:    bool   — if false, exits with port "not_required"
///     anticipoPercentage:  int    — percentage of the total amount (default: 50)
///     currency:            string — currency code (default: "COP")
///     expirationMinutes:   int    — link validity in minutes (default: 1440 = 24h)
///
/// Output keys:
///   link_url              — payment link URL
///   reference_id          — payment reference ID
///   anticipo_amount_cents — raw anticipo amount in cents (string, InvariantCulture)
///
/// Requires <c>total_price_invariant</c> in state (populated by <c>resolve_pricing</c>).
/// Output ports: success, failure, not_required
/// </summary>
public class GeneratePaymentLinkAction : IFlowAction
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IPaymentLinkService _paymentLinkService;
    private readonly ILogger<GeneratePaymentLinkAction> _logger;

    public string ActionType => "generate_payment_link";

    public GeneratePaymentLinkAction(
        IPaymentLinkService paymentLinkService,
        ILogger<GeneratePaymentLinkAction> logger)
    {
        _paymentLinkService = paymentLinkService;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        // Read payment config from node config block (forwarded as JSON string by ActionNodeHandler)
        var paymentConfig = ParsePaymentConfig(inputs.GetString("payment"));

        if (paymentConfig?.RequiresAnticipo != true)
        {
            _logger.LogInformation("GeneratePaymentLink: payment not required for agent {AgentId}", ctx.Agent.AgentId);
            return FlowActionResult.Custom("not_required");
        }

        var item = inputs.GetString("item");
        if (string.IsNullOrEmpty(item))
        {
            _logger.LogWarning("GeneratePaymentLink: missing item");
            return FlowActionResult.Failed("item is required");
        }

        var totalInvariant = ctx.State.GetVariable("total_price_invariant");
        if (string.IsNullOrWhiteSpace(totalInvariant)
            || !decimal.TryParse(totalInvariant, NumberStyles.Any, CultureInfo.InvariantCulture, out var orderTotal)
            || orderTotal <= 0)
        {
            _logger.LogWarning(
                "GeneratePaymentLink: missing or invalid total_price_invariant (run resolve_pricing before payment)");
            return FlowActionResult.Failed("Total price not available");
        }

        var pct = paymentConfig.AnticipoPercentage is > 0 and <= 100
            ? paymentConfig.AnticipoPercentage
            : 50;
        var fraction = pct / 100m;
        // Misma fórmula que HybridTransactionalOrchestrator: total * porcentaje * 100
        var anticipoCents = (long)(orderTotal * fraction * 100);

        try
        {
            var phone = ctx.State.GetVariable("phone") ?? ctx.UserIdentifier;
            var request = new PaymentLinkRequest(
                BusinessId: ctx.BusinessId,
                ConversationId: ctx.ConversationId,
                CustomerPhone: phone,
                ServiceDescription: item,
                AmountInCents: anticipoCents,
                Currency: paymentConfig.Currency,
                ExpirationMinutes: paymentConfig.ExpirationMinutes
            );

            var result = await _paymentLinkService.GenerateAnticipoLinkAsync(request, ct);

            if (!result.Success)
                return FlowActionResult.Failed(result.ErrorMessage ?? "Payment link generation failed");

            return FlowActionResult.Succeeded(new Dictionary<string, object?>
            {
                ["link_url"] = result.PaymentLinkUrl,
                ["reference_id"] = result.PaymentReferenceId,
                ["anticipo_amount_cents"] = anticipoCents.ToString(CultureInfo.InvariantCulture)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeneratePaymentLink failed for item={Item}", item);
            return FlowActionResult.Failed(ex.Message);
        }
    }

    private NodePaymentConfig? ParsePaymentConfig(string? paymentJson)
    {
        if (string.IsNullOrWhiteSpace(paymentJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NodePaymentConfig>(paymentJson, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeneratePaymentLink: failed to parse 'payment' config block");
            return null;
        }
    }

    private sealed class NodePaymentConfig
    {
        public bool RequiresAnticipo { get; set; }
        public int AnticipoPercentage { get; set; } = 50;
        public string Currency { get; set; } = "COP";
        public int ExpirationMinutes { get; set; } = 1440;
    }
}
