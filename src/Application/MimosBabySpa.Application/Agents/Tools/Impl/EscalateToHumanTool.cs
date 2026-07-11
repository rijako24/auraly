using System.Text.Json;
using MimosBabySpa.Application.Services;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("escalate_to_human", Capabilities = new[] { ToolCapabilities.HumanEscalate })]
public sealed class EscalateToHumanTool : IAgentTool
{
    private readonly IEscalationNotifier _escalationNotifier;
    public EscalateToHumanTool(IEscalationNotifier escalationNotifier) =>
        _escalationNotifier = escalationNotifier;

    public string Name => "escalate_to_human";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.HumanEscalate];

    public string Description =>
        "Notifies configured human escalation contacts without disabling the bot or changing conversation ownership.";

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
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "reason", out var reason);
        ToolResultHelper.TryGetString(arguments, "last_user_message", out var lastUserMessage);
        ctx.ConversationState.LastEscalatedAt = DateTime.UtcNow;
        var contactPhone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone, ctx.Config) ?? string.Empty;

        if (ctx.EscalationContacts.Count > 0)
        {
            try
            {
                var notification = new EscalationNotification(
                    ConversationId: ctx.ConversationId,
                    CustomerPhone: contactPhone,
                    Reason: reason ?? "agent_request",
                    LastUserMessage: lastUserMessage);

                await _escalationNotifier.NotifyAsync(
                    ctx.BusinessId, ctx.EscalationContacts, notification, cancellationToken);
            }
            catch
            {
                // Fallo de notificacion no debe romper el turno del bot.
            }
        }

        return ToolResultHelper.Ok(new
        {
            escalated = true,
            reason,
            message = "Human escalation contacts have been notified; the bot remains active."
        }, EscalatedToHuman);
    }
}
