using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class ApplyOrderChangesOperation : IAgentOperation
{
    public const string OperationId = "commerce.apply_order_changes";

    private readonly CartCommandBatchProcessor _processor;

    public ApplyOrderChangesOperation(CartCommandBatchProcessor processor) => _processor = processor;

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "commands": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": { "type": "string", "enum": ["add", "remove", "set_quantity"] },
                  "productText": { "type": "string" },
                  "quantity": { "type": ["number", "null"] },
                  "groupReference": { "type": ["string", "null"] }
                },
                "required": ["operation", "productText", "quantity", "groupReference"]
              }
            }
          },
          "required": ["commands"]
        }
        """,
        [
            "cart.applied",
            "cart.no_changes",
            "cart.conflicting_commands",
            "cart.multiple_orders",
            "cart.product_not_found",
            "cart.product_ambiguous",
            "cart.item_not_found_or_ambiguous",
            "cart.invalid_input"
        ],
        ["commerce.order_draft.write"],
        [],
        ["order_intake"]);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!input.TryGetProperty("commands", out var commandsElement)
            || commandsElement.ValueKind != JsonValueKind.Array)
            return OperationOutcome.Fail("cart.invalid_input", "commands must be an array.", true);

        List<CartCommand>? commands;
        try
        {
            commands = JsonSerializer.Deserialize<List<CartCommand>>(
                commandsElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            return OperationOutcome.Fail("cart.invalid_input", exception.Message, true);
        }

        if (commands is null)
            return OperationOutcome.Fail("cart.invalid_input", "commands could not be parsed.", true);

        var session = context.Session ?? new AgentToolContext
        {
            AgentId = context.AgentId,
            BusinessId = context.BusinessId,
            ConversationId = context.ConversationId,
            BusinessToday = context.BusinessToday,
            BusinessNow = context.BusinessNow,
            Config = context.Config,
            ConversationState = context.ConversationState,
            Facts = new Dictionary<string, string>(context.Facts, StringComparer.OrdinalIgnoreCase)
        };

        var result = await _processor.ApplyAsync(session, commands, cancellationToken);
        return result.Success
            ? OperationOutcome.Ok(result.Code, new { order = result.Snapshot })
            : OperationOutcome.Fail(
                result.Code,
                result.Code == "cart.multiple_orders"
                    ? "Only one order can be active at a time. Tell the customer to finish the current order before starting another one; no changes were applied."
                    : "The requested order changes could not be applied atomically.",
                true,
                "order_changes_clarification",
                new { issues = result.Issues });
    }
}
