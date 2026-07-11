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
                  "destinationReference": { "type": ["string", "null"] }
                },
                "required": ["operation", "productText", "quantity", "destinationReference"]
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
            "cart.multiple_destinations",
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

        var session = context.Session ?? new AgentConversationContext
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
            ? OperationOutcome.Ok(result.Code, new
            {
                order = result.Snapshot is null ? null : new
                {
                    currency = result.Snapshot.Currency,
                    subtotal = result.Snapshot.Subtotal,
                    discount_total = result.Snapshot.DiscountTotal,
                    tax_total = result.Snapshot.TaxTotal,
                    total = result.Snapshot.Total,
                    items = result.Snapshot.Items.Select(item => new
                    {
                        name = item.ProductName,
                        quantity = item.Quantity,
                        unit_price = item.UnitPrice,
                        line_total = item.LineTotal
                    }).ToList()
                }
            })
            : OperationOutcome.Fail(
                result.Code,
                result.Code == "cart.multiple_destinations"
                    ? "Only one delivery address can apply to the active order. Ask which provided address should be used for the whole order; no changes were applied."
                    : "The requested order changes could not be applied atomically.",
                true,
                "order_changes_clarification",
                new
                {
                    issues = result.Issues,
                    product_text = result.Issues.FirstOrDefault()?.ProductText,
                    candidates = result.Issues.FirstOrDefault()?.Candidates ?? [],
                    product_options = result.Issues.FirstOrDefault()?.ProductCandidates ?? []
                });
    }
}
