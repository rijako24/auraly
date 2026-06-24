using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Clears the current request context while preserving facts marked as persistent in factSchema.
/// </summary>
public sealed class ResetFlowContextTool : IAgentTool
{
    private readonly IRequestContextService _requestContext;
    private readonly IPaymentLifecycleService _payments;
    private readonly IUnitOfWork _unitOfWork;

    public ResetFlowContextTool(
        IRequestContextService requestContext,
        IPaymentLifecycleService payments,
        IUnitOfWork unitOfWork)
    {
        _requestContext = requestContext;
        _payments = payments;
        _unitOfWork = unitOfWork;
    }

    public string Name => "reset_flow_context";

    public string Description =>
        "Reinicia la solicitud actual limpiando los facts no persistentes y verificaciones del flujo. " +
        "Conserva los facts con scope=customer. " +
        "Puede abandonar un checkout/link pendiente si checkout_action='abandon'.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reason": {
              "type": "string",
              "description": "Reason for audit/context, e.g. start_new_request or customer_abandoned"
            },
            "checkout_action": {
              "type": "string",
              "enum": ["none", "abandon"],
              "description": "Use abandon when the customer leaves the current pending checkout without replacing it"
            }
          },
          "required": ["reason"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "reason", out var reason)
            || string.IsNullOrWhiteSpace(reason))
        {
            return ToolResultHelper.MissingPrerequisites(["reason"]);
        }

        ToolResultHelper.TryGetString(arguments, "checkout_action", out var checkoutAction);
        checkoutAction = string.IsNullOrWhiteSpace(checkoutAction) ? "none" : checkoutAction.Trim();

        if (!checkoutAction.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !checkoutAction.Equals("abandon", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Error(
                "invalid_checkout_action",
                $"checkout_action '{checkoutAction}' is not supported.",
                "Use 'none' or 'abandon'.");
        }

        PaymentTransactionStatus? previousPaymentStatus = null;
        Guid? abandonedPaymentId = null;
        Guid? deletedOrderDraftId = null;

        if (checkoutAction.Equals("abandon", StringComparison.OrdinalIgnoreCase))
        {
            var activePayment = ctx.ActivePayment
                ?? await _payments.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);

            if (activePayment is not null)
            {
                previousPaymentStatus = activePayment.Status;
                if (activePayment.Status == PaymentTransactionStatus.Created)
                {
                    await _payments.MarkAbandonedAsync(activePayment, cancellationToken);
                    abandonedPaymentId = activePayment.PaymentTransactionId;
                    ctx.ActivePayment = null;
                }
            }
        }

        if (ctx.BusinessId != Guid.Empty && ctx.ConversationId != Guid.Empty)
        {
            var activeDrafts = await _unitOfWork.OrderDrafts.GetActiveDraftsByConversationAsync(
                ctx.BusinessId,
                ctx.ConversationId,
                cancellationToken);

            foreach (var activeDraft in activeDrafts)
            {
                deletedOrderDraftId ??= activeDraft.OrderDraftId;
                await _unitOfWork.OrderDrafts.DeleteAsync(activeDraft, cancellationToken);
            }
            if (deletedOrderDraftId.HasValue)
                await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var cleanup = await _requestContext.CompleteAsync(
            ctx.ConversationId,
            ctx.Config ?? new AgentConfig(),
            ctx.ConversationState,
            ctx.Facts,
            reason.Trim(),
            cancellationToken);

        return ToolResultHelper.Ok(new
        {
            reason,
            checkout_action = checkoutAction,
            cleared_facts = cleanup.ClearedFacts,
            preserved_facts = cleanup.PreservedFacts,
            abandoned_payment_transaction_id = abandonedPaymentId,
            previous_payment_status = previousPaymentStatus?.ToString(),
            deleted_order_draft_id = deletedOrderDraftId
        });
    }
}
