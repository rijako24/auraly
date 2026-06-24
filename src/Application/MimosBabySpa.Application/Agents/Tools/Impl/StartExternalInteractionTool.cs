using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class StartExternalInteractionTool : IAgentTool
{
    private readonly IExternalEscalationService _interactions;

    public StartExternalInteractionTool(IExternalEscalationService interactions)
    {
        _interactions = interactions;
    }

    public string Name => "start_external_interaction";

    public string Description =>
        "Starts a configured external interaction with another contact. Use it when the current flow needs an external contact to resolve a request, answer a question, approve, or provide data.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "event_name": { "type": "string" },
            "target_type": { "type": "string" },
            "target_id": { "type": "string" },
            "question": { "type": "string" },
            "context": { "type": "string" }
          },
          "required": ["event_name"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "event_name", out var eventName))
            return ToolResultHelper.Error("event_name_required", "event_name is required.");

        var targetType = ToolResultHelper.TryGetString(arguments, "target_type", out var targetTypeArg)
            ? targetTypeArg
            : "conversation";

        if (ctx.Config is null)
            return ToolResultHelper.Error("agent_config_required", "Agent configuration is required.");

        var targetId = ResolveTargetId(arguments, ctx);
        ToolResultHelper.TryGetString(arguments, "question", out var question);
        ToolResultHelper.TryGetString(arguments, "context", out var context);

        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source_conversation_id"] = ctx.ConversationId.ToString(),
            ["source_agent_id"] = ctx.Config.AgentId.ToString(),
            ["question"] = question ?? string.Empty,
            ["context"] = context ?? string.Empty
        };

        var result = await _interactions.EscalateNextAsync(
            new ExternalEscalationRequest(
                ctx.Config.AgentId,
                eventName,
                targetType,
                targetId,
                custom),
            cancellationToken);

        if (!result.Sent)
            return ToolResultHelper.Error(result.Error ?? "external_interaction_not_sent", "External interaction could not be sent.");

        return ToolResultHelper.Ok(new
        {
            sent = true,
            event_name = eventName,
            target_type = targetType,
            target_id = targetId,
            external_interaction_id = result.InteractionId,
            external_escalation_id = result.InteractionId,
            attempt_code = result.Code
        });
    }

    private static Guid ResolveTargetId(JsonElement arguments, AgentToolContext ctx)
    {
        return ToolResultHelper.TryGetString(arguments, "target_id", out var targetIdArg)
            && Guid.TryParse(targetIdArg, out var parsed)
                ? parsed
                : ctx.ConversationId;
    }
}

