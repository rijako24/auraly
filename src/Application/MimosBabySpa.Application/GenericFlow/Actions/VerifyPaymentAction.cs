using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Verifies payment status against the payment gateway.
/// Input keys: reference_id
/// Output keys: confirmed (bool string: "true"/"false"), transaction_id (string?)
/// Output ports: success (confirmed), failure (not confirmed or error)
/// </summary>
public class VerifyPaymentAction : IFlowAction
{
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly ILogger<VerifyPaymentAction> _logger;

    public string ActionType => "verify_payment";

    public VerifyPaymentAction(IPaymentLinkService paymentLinkService, ILogger<VerifyPaymentAction> logger)
    {
        _paymentLinkService = paymentLinkService;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var referenceId = inputs.GetString("reference_id");
        if (string.IsNullOrEmpty(referenceId))
        {
            _logger.LogWarning("VerifyPayment: missing reference_id");
            return FlowActionResult.Failed("reference_id is required");
        }

        try
        {
            var status = await _paymentLinkService.CheckPaymentStatusAsync(referenceId, ctx.BusinessId, ct);

            if (status.IsApproved)
            {
                return FlowActionResult.Succeeded(new Dictionary<string, object?>
                {
                    ["confirmed"] = "true",
                    ["transaction_id"] = status.TransactionId
                });
            }

            _logger.LogDebug("VerifyPayment: payment not yet confirmed for ref={Ref}", referenceId);
            return FlowActionResult.Failed("Payment not yet confirmed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VerifyPayment failed for ref={Ref}", referenceId);
            return FlowActionResult.Failed(ex.Message);
        }
    }
}
