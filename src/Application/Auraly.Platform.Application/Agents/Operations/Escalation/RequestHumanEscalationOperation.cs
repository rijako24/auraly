using System.Text.Json;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Escalation;

public sealed class RequestHumanEscalationOperation : IAgentOperation
{
    private readonly IEscalationNotifier _notifier;

    public RequestHumanEscalationOperation(IEscalationNotifier notifier) => _notifier = notifier;

    public OperationDescriptor Descriptor { get; } = new(
        "escalation.request_human",
        """{"type":"object","additionalProperties":false,"properties":{"reason":{"type":"string"},"last_user_message":{"type":"string"}},"required":["reason","last_user_message"]}""",
        ["escalation.requested", "escalation.notification_failed"],
        ["conversation.escalate"], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session
            ?? throw new InvalidOperationException("escalation.request_human requires a conversation session.");
        var reason = input.GetProperty("reason").GetString() ?? "customer_request";
        var message = input.GetProperty("last_user_message").GetString();
        var phone = ConversationContactPhone.Resolve(session.Facts, session.ChannelPhone, context.Config) ?? string.Empty;

        try
        {
            if (session.EscalationContacts.Count > 0)
            {
                await _notifier.NotifyAsync(
                    context.BusinessId,
                    session.EscalationContacts,
                    new EscalationNotification(context.ConversationId, phone, reason, message),
                    cancellationToken);
            }

            context.ConversationState.LastEscalatedAt = DateTime.UtcNow;
            return OperationOutcome.Ok(
                "escalation.requested",
                new { escalated = true, reason },
                effects: [new EscalateHumanOperationEffect()]);
        }
        catch (Exception exception)
        {
            return OperationOutcome.Fail(
                "escalation.notification_failed",
                exception.Message,
                true,
                "retry_human_escalation");
        }
    }
}
