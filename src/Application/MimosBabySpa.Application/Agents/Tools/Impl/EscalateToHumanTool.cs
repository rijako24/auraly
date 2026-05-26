using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class EscalateToHumanTool : IAgentTool
{
    private readonly IEscalationNotifier _escalationNotifier;

    public EscalateToHumanTool(IEscalationNotifier escalationNotifier) =>
        _escalationNotifier = escalationNotifier;

    public string Name => "escalate_to_human";

    public IReadOnlyList<string> SemanticTriggers =>
    [
        "customer_frustration",
        "consecutive_errors",
        "out_of_scope_request",
        "explicit_human_request"
    ];

    public string Description =>
        "Marks the conversation as owned by a human agent and notifies configured escalation contacts.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reason": { "type": "string" },
            "last_user_message": { "type": "string" }
          },
          "required": ["reason"]
        }
        """;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(invocation.Arguments, "reason", out var reason);
        ToolResultHelper.TryGetString(invocation.Arguments, "last_user_message", out var lastUserMessage);

        var ctx = invocation.Context;
        ctx.ConversationState.Owner = ConversationOwner.Human;
        ctx.ConversationState.LastEscalatedAt = DateTime.UtcNow;

        var contactPhone = ConversationContactPhone.Resolve(ctx) ?? string.Empty;
        var activePayment = ctx.GetPackContext<IBookingPackContext>()?.ActivePayment;

        if (ctx.EscalationContacts.Count > 0)
        {
            try
            {
                var notification = new EscalationNotification(
                    ConversationId: ctx.ConversationId,
                    CustomerPhone: contactPhone,
                    Reason: reason ?? "agent_request",
                    LastUserMessage: lastUserMessage,
                    PaymentReferenceId: activePayment?.PaymentReferenceId);

                await _escalationNotifier.NotifyAsync(
                    ctx.BusinessId, ctx.EscalationContacts, notification, cancellationToken);
            }
            catch
            {
                // Fallo de notificación no revierte Owner=Human
            }
        }

        return ToolResultHelper.Ok(new
        {
            escalated = true,
            reason,
            message = "The conversation has been transferred to a human agent."
        }, EscalatedToHuman);
    }
}
