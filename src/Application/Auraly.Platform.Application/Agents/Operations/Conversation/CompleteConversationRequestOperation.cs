using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Operations.Conversation;

public sealed class CompleteConversationRequestOperation : IAgentOperation
{
    private const string InputSchema = """
        {
          "type": "object",
          "properties": {
            "confirmed": { "type": "boolean" }
          },
          "required": ["confirmed"],
          "additionalProperties": false
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        "conversation.complete_request",
        InputSchema,
        ["request.completed", "request.confirmation_required"],
        [],
        [],
        []);

    public Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var confirmed = input.TryGetProperty("confirmed", out var value)
            && value.ValueKind == JsonValueKind.True;
        return Task.FromResult(confirmed
            ? OperationOutcome.Ok(
                "request.completed",
                new { completed = true },
                effects: [new CompleteRequestOperationEffect()])
            : OperationOutcome.Ok(
                "request.confirmation_required",
                new { completed = false }));
    }
}
