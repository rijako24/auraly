using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class ResolveExternalEscalationTool : IAgentTool
{
    private readonly IExternalEscalationService _escalations;

    public ResolveExternalEscalationTool(IExternalEscalationService attempts)
    {
        _escalations = attempts;
    }

    public string Name => "resolve_external_escalation";

    public string Description =>
        "Resolves which external escalation attempt the current contact message refers to. " +
        "Uses WhatsApp button payload, quoted message id, attempt code, or the only open attempt for this contact.";

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
                pending_attempts = result.PendingAttempts.Select(o => new
                {
                    external_escalation_id = o.ExternalEscalationAttemptId,
                    attempt_code = o.AttemptCode,
                    event_name = o.EventName,
                    target_type = o.TargetType,
                    target_id = o.TargetId,
                    order_number = ReadCustomValue(o.CustomPayloadJson, "order_number"),
                    customer_name = ReadCustomValue(o.CustomPayloadJson, "customer_name"),
                    customer_phone = ReadCustomValue(o.CustomPayloadJson, "customer_phone"),
                    delivery_address = ReadCustomValue(o.CustomPayloadJson, "delivery_address"),
                    total = ReadCustomValue(o.CustomPayloadJson, "total"),
                    currency = ReadCustomValue(o.CustomPayloadJson, "currency")
                }).ToList()
            });
        }

        return ToolResultHelper.Ok(new
        {
            resolution = "resolved",
            requested_action = result.RequestedAction,
            external_escalation_id = result.Attempt.ExternalEscalationAttemptId,
            attempt_code = result.Attempt.AttemptCode,
            event_name = result.Attempt.EventName,
            target_type = result.Attempt.TargetType,
            target_id = result.Attempt.TargetId,
            order_number = ReadCustomValue(result.Attempt.CustomPayloadJson, "order_number"),
            customer_name = ReadCustomValue(result.Attempt.CustomPayloadJson, "customer_name"),
            customer_phone = ReadCustomValue(result.Attempt.CustomPayloadJson, "customer_phone"),
            delivery_address = ReadCustomValue(result.Attempt.CustomPayloadJson, "delivery_address"),
            total = ReadCustomValue(result.Attempt.CustomPayloadJson, "total"),
            currency = ReadCustomValue(result.Attempt.CustomPayloadJson, "currency")
        });
    }

    private static string? ReadCustomValue(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}