using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class CompleteExternalInteractionTool : IAgentTool
{
    private readonly IExternalEscalationService _interactions;

    public CompleteExternalInteractionTool(IExternalEscalationService interactions)
    {
        _interactions = interactions;
    }

    public string Name => "complete_external_interaction";

    public string Description =>
        "Completes a resolved external interaction for the current contact with a generic outcome and optional response text.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "external_interaction_id": { "type": "string" },
            "outcome_key": { "type": "string" },
            "response_text": { "type": "string" },
            "response_payload": {
              "type": "object",
              "additionalProperties": { "type": ["string", "number", "boolean", "null"] }
            }
          },
          "required": ["outcome_key"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var attemptId = ResolveAttemptId(arguments, ctx);
        if (attemptId is null)
            return ToolResultHelper.Error("external_interaction_required", "external_interaction_id is required.");

        if (!ToolResultHelper.TryGetString(arguments, "outcome_key", out var outcomeKey))
            return ToolResultHelper.Error("outcome_required", "outcome_key is required.");

        ToolResultHelper.TryGetString(arguments, "response_text", out var responseText);
        var responsePayload = ReadResponsePayload(arguments);

        var result = await _interactions.CompleteAsync(
            ctx.BusinessId,
            attemptId.Value,
            ctx.ChannelPhone,
            outcomeKey,
            responseText,
            responsePayload,
            cancellationToken);

        if (!result.Success)
            return ToolResultHelper.Error("external_interaction_not_available", result.Message);

        return ToolResultHelper.Ok(ToPayload(result), ToolSideEffectNames.RequestCompleted);
    }

    private static object ToPayload(ExternalEscalationActionResult result) => new
    {
        completed = true,
        external_interaction_id = result.Attempt?.ExternalEscalationAttemptId,
        attempt_code = result.Attempt?.AttemptCode,
        event_name = result.Attempt?.EventName,
        target_type = result.Attempt?.TargetType,
        target_id = result.Attempt?.TargetId,
        outcome_key = result.OutcomeKey,
        response_text = result.ResponseText,
        payload = result.Payload
    };

    private static Guid? ResolveAttemptId(JsonElement arguments, AgentToolContext ctx)
    {
        if (ToolResultHelper.TryGetString(arguments, "external_interaction_id", out var interactionId)
            && Guid.TryParse(interactionId, out var parsed))
        {
            return parsed;
        }

        return ctx.Facts.TryGetValue("external_interaction_id", out var interactionFact)
            && Guid.TryParse(interactionFact, out parsed)
            ? parsed
            : null;
    }
    private static IReadOnlyDictionary<string, string>? ReadResponsePayload(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("response_payload", out var payload)
            || payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in payload.EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText()
            };
        }

        return values;
    }
}
