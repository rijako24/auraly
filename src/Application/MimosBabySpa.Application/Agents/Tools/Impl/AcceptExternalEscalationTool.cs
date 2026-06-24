using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class AcceptExternalEscalationTool : IAgentTool
{
    private readonly IExternalEscalationService _escalations;

    public AcceptExternalEscalationTool(IExternalEscalationService attempts)
    {
        _escalations = attempts;
    }

    public string Name => "accept_external_escalation";

    public string Description => "Compatibility wrapper that completes a resolved external interaction with outcome_key=accepted.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "external_escalation_id": { "type": "string" },
            "external_interaction_id": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var attemptId = ResolveAttemptId(arguments, ctx);
        if (attemptId is null)
            return ToolResultHelper.Error("external_interaction_required", "external_interaction_id is required.");

        var result = await _escalations.AcceptAsync(ctx.BusinessId, attemptId.Value, ctx.ChannelPhone, cancellationToken);
        if (!result.Success)
            return ToolResultHelper.Error("external_interaction_not_available", result.Message);

        return ToolResultHelper.Ok(new
        {
            completed = true,
            accepted = true,
            external_interaction_id = result.Attempt?.ExternalEscalationAttemptId,
            external_escalation_id = result.Attempt?.ExternalEscalationAttemptId,
            attempt_code = result.Attempt?.AttemptCode,
            event_name = result.Attempt?.EventName,
            target_type = result.Attempt?.TargetType,
            target_id = result.Attempt?.TargetId,
            outcome_key = result.OutcomeKey,
            payload = result.Payload
        }, ToolSideEffectNames.RequestCompleted);
    }

    private static Guid? ResolveAttemptId(JsonElement arguments, AgentToolContext ctx)
    {
        if (ToolResultHelper.TryGetString(arguments, "external_interaction_id", out var interactionId)
            && Guid.TryParse(interactionId, out var parsed))
        {
            return parsed;
        }

        if (ToolResultHelper.TryGetString(arguments, "external_escalation_id", out var escalationId)
            && Guid.TryParse(escalationId, out parsed))
        {
            return parsed;
        }

        return ctx.Facts.TryGetValue("external_interaction_id", out var interactionFact)
            && Guid.TryParse(interactionFact, out parsed)
            ? parsed
            : ctx.Facts.TryGetValue("external_escalation_id", out var escalationFact)
                && Guid.TryParse(escalationFact, out parsed)
                ? parsed
                : null;
    }
}
