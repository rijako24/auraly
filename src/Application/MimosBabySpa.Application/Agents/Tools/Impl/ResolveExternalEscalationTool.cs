using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public class ResolveExternalEscalationTool : IAgentTool
{
    private readonly IExternalEscalationService _escalations;
    private readonly string _name;

    public ResolveExternalEscalationTool(IExternalEscalationService attempts)
        : this(attempts, "resolve_external_escalation")
    {
    }

    protected ResolveExternalEscalationTool(IExternalEscalationService attempts, string name)
    {
        _escalations = attempts;
        _name = name;
    }

    public string Name => _name;

    public string Description =>
        "Resolves which external interaction the current contact message refers to. " +
        "Uses WhatsApp button payload, quoted message id, attempt code, or the only open interaction for this contact.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "message_text": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "message_text", out var text);
        text ??= ctx.ConversationState.LastUserMessage ?? string.Empty;

        var result = await _escalations.ResolveAttemptAsync(
            ctx.BusinessId,
            ctx.ChannelPhone,
            text,
            ctx.InteractivePayload,
            ctx.ReplyToProviderMessageId,
            cancellationToken);

        if (result.Attempt is null)
        {
            return ToolResultHelper.Ok(new
            {
                resolution = result.Resolution,
                error = result.Error,
                requested_action = result.RequestedAction,
                pending_interactions = result.PendingAttempts.Select(ToInteractionPayload).ToList(),
                pending_attempts = result.PendingAttempts.Select(ToInteractionPayload).ToList()
            });
        }

        return ToolResultHelper.Ok(new
        {
            resolution = "resolved",
            requested_action = result.RequestedAction,
            interaction = ToInteractionPayload(result.Attempt),
            external_interaction_id = result.Attempt.ExternalEscalationAttemptId,
            external_escalation_id = result.Attempt.ExternalEscalationAttemptId,
            attempt_code = result.Attempt.AttemptCode,
            event_name = result.Attempt.EventName,
            target_type = result.Attempt.TargetType,
            target_id = result.Attempt.TargetId,
            custom_payload = ReadCustomPayload(result.Attempt.CustomPayloadJson)
        });
    }

    private static object ToInteractionPayload(Domain.Entities.ExternalEscalationAttempt attempt) => new
    {
        external_interaction_id = attempt.ExternalEscalationAttemptId,
        external_escalation_id = attempt.ExternalEscalationAttemptId,
        attempt_code = attempt.AttemptCode,
        event_name = attempt.EventName,
        target_type = attempt.TargetType,
        target_id = attempt.TargetId,
        contact_key = attempt.ContactKey,
        contact_name = attempt.ContactNameSnapshot,
        contact_role = attempt.ContactRoleSnapshot,
        custom_payload = ReadCustomPayload(attempt.CustomPayloadJson)
    };

    private static IReadOnlyDictionary<string, string> ReadCustomPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
public sealed class ResolveExternalInteractionTool : ResolveExternalEscalationTool
{
    public ResolveExternalInteractionTool(IExternalEscalationService attempts)
        : base(attempts, "resolve_external_interaction")
    {
    }
}