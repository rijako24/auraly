using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class DeclineExternalEscalationTool : IAgentTool
{
    private readonly IExternalEscalationService _escalations;

    public DeclineExternalEscalationTool(IExternalEscalationService attempts)
    {
        _escalations = attempts;
    }

    public string Name => "decline_external_escalation";

    public string Description =>
        "Declines a resolved external escalation attempt for the current contact and escalates the same target to the next configured contact.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "external_escalation_id": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var attemptId = ResolveAttemptId(arguments, ctx);
        if (attemptId is null)
            return ToolResultHelper.Error("external_escalation_required", "external_escalation_id is required.");

        var result = await _escalations.DeclineAsync(ctx.BusinessId, attemptId.Value, ctx.ChannelPhone, cancellationToken);
        if (!result.Success)
            return ToolResultHelper.Error("external_escalation_not_available", result.Message);

        return ToolResultHelper.Ok(new
        {
            declined = true,
            escalated_next = result.EscalatedNext,
            external_escalation_id = result.Attempt?.ExternalEscalationAttemptId,
            attempt_code = result.Attempt?.AttemptCode,
            event_name = result.Attempt?.EventName,
            target_type = result.Attempt?.TargetType,
            target_id = result.Attempt?.TargetId,
            message = result.Message
        });
    }

    private static Guid? ResolveAttemptId(JsonElement arguments, AgentToolContext ctx)
    {
        if (ToolResultHelper.TryGetString(arguments, "external_escalation_id", out var fromArgs)
            && Guid.TryParse(fromArgs, out var parsed))
        {
            return parsed;
        }

        return ctx.Facts.TryGetValue("external_escalation_id", out var fact)
            && Guid.TryParse(fact, out parsed)
            ? parsed
            : null;
    }
}
