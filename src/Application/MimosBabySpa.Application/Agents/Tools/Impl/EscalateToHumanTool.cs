using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Escala la conversación a un agente humano.
/// Establece Owner=Human en el estado (inhibe el bot), notifica a los contactos
/// configurados en Agent.SettingsJson → escalation.contacts[].
/// </summary>
public sealed class EscalateToHumanTool : IAgentTool
{
    private readonly IConversationStateManager _stateManager;
    private readonly IEscalationNotifier _escalationNotifier;

    public EscalateToHumanTool(
        IConversationStateManager stateManager,
        IEscalationNotifier escalationNotifier)
    {
        _stateManager = stateManager;
        _escalationNotifier = escalationNotifier;
    }

    public string Name => "escalate_to_human";

    public string Description =>
        "Transfers the conversation to a human agent. " +
        "Call this when: the customer explicitly requests a human, the customer is upset, " +
        "or after 2+ failed attempts to resolve an issue.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reason": {
              "type": "string",
              "description": "Brief reason for escalation (e.g. 'customer_request', 'payment_issue', 'complaint')"
            },
            "last_user_message": {
              "type": "string",
              "description": "Last message from the user (for context in the notification)"
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
        ToolResultHelper.TryGetString(arguments, "reason", out var reason);
        ToolResultHelper.TryGetString(arguments, "last_user_message", out var lastUserMessage);

        var state = await _stateManager.GetOrCreateStateAsync(
            ctx.ConversationId, ctx.BusinessId, ctx.CustomerPhone, cancellationToken);

        state.Owner = ConversationOwner.Human;
        state.LastEscalatedAt = DateTime.UtcNow;
        await _stateManager.SaveStateAsync(ctx.ConversationId, state, cancellationToken);

        if (ctx.EscalationContacts.Count > 0)
        {
            try
            {
                var notification = new EscalationNotification(
                    ConversationId: ctx.ConversationId,
                    CustomerPhone: ctx.CustomerPhone,
                    Reason: reason ?? "agent_request",
                    LastUserMessage: lastUserMessage,
                    PaymentReferenceId: state.PaymentReferenceId);

                await _escalationNotifier.NotifyAsync(
                    ctx.BusinessId, ctx.EscalationContacts, notification, cancellationToken);
            }
            catch
            {
                // Fallo de notificación no revierte el Owner=Human
            }
        }

        return ToolResultHelper.Ok(new
        {
            escalated = true,
            reason,
            message = "The conversation has been transferred to a human agent."
        });
    }
}
