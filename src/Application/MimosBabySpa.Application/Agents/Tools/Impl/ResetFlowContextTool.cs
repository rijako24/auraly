using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Clears the current request context while preserving facts marked as persistent in factSchema.
/// </summary>
public sealed class ResetFlowContextTool : IAgentTool
{
    private readonly IConversationFactsService _facts;
    private readonly IPaymentLifecycleService _payments;

    public ResetFlowContextTool(
        IConversationFactsService facts,
        IPaymentLifecycleService payments)
    {
        _facts = facts;
        _payments = payments;
    }

    public string Name => "reset_flow_context";

    public string Description =>
        "Reinicia la solicitud actual limpiando los facts no persistentes y verificaciones del flujo. " +
        "Conserva los facts marcados como persistsAcrossConversations=true. " +
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

        var persistentKeys = (ctx.Config?.FactSchema ?? [])
            .Where(f => f.PersistsAcrossConversations)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var clearedFacts = await _facts.ClearNonPersistentAsync(
            ctx.ConversationId,
            persistentKeys,
            cancellationToken);

        foreach (var key in clearedFacts)
            ctx.Facts.Remove(key);

        ctx.ConversationState.Verifications.Clear();
        ctx.ConversationState.StageFactSnapshots.Clear();

        return ToolResultHelper.Ok(new
        {
            reason,
            checkout_action = checkoutAction,
            cleared_facts = clearedFacts,
            preserved_facts = persistentKeys,
            abandoned_payment_transaction_id = abandonedPaymentId,
            previous_payment_status = previousPaymentStatus?.ToString()
        });
    }
}
