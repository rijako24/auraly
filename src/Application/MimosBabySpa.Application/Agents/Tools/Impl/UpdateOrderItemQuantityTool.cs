using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("update_order_item_quantity", Capabilities = new[] { ToolCapabilities.OrderDraftUpdate })]
public sealed class UpdateOrderItemQuantityTool : IAgentTool
{
private readonly ICommerceService _commerce;
    private readonly IConversationFactsService _factsService;

    public UpdateOrderItemQuantityTool(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    public string Name => "update_order_item_quantity";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderDraftUpdate];
    public IReadOnlyList<string> OperatingGroups => [ToolOperatingGroups.OrderIntake];
    public string Description =>
        "Sets an existing order draft item to an explicit final quantity requested by the customer. " +
        "Use this when the customer says they want to change, update, leave, or carry a total number of units for an item already in the cart. " +
        "Call get_order_draft first when the current cart contents or order_item_id are unknown. " +
        "Do not use this to add another product or additional units; use add_order_item for additions.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "order_item_id": { "type": "string", "description": "order_item_id from get_order_draft." },
            "product_id": { "type": "string", "description": "Catalog product_id when order_item_id is unavailable." },
            "external_product_id": { "type": "string" },
            "sku": { "type": "string" },
            "name": { "type": "string" },
            "quantity": {
              "type": "number",
              "minimum": 0,
              "description": "Exact final quantity the item should have after the update. Use 0 only when the customer wants none left."
            }
          },
          "required": ["quantity"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        if (!TryGetDecimal(arguments, "quantity", out var quantity))
            return ToolResultHelper.MissingPrerequisites(["quantity"]);

        var draft = await _commerce.GetDraftAsync(ctx, cancellationToken);
        if (draft.Items.Count == 0)
            return ToolResultHelper.ErrorWithNextAction("empty_order", "The order draft has no items.", "select_product", recoverable: true);

        if (!OrderItemSelectionResolver.TryResolve(arguments, draft, out var item, out var ambiguous))
        {
            if (ambiguous.Count > 1)
            {
                return ToolResultHelper.ErrorWithNextAction(
                    "order_item_ambiguous",
                    "The order item selection is ambiguous.",
                    "clarify_order_item_selection",
                    new { available_items = ToSelectionOptions(ambiguous) },
                    recoverable: true);
            }

            return ToolResultHelper.MissingPrerequisites(["order_item_id"]);
        }

        var updated = await _commerce.UpdateItemQuantityAsync(ctx, item.OrderItemId, quantity, cancellationToken);
        await OrderDraftFactInvalidation.ClearOrderFinalizedAsync(_factsService, ctx, cancellationToken);

        return ToolResultHelper.Ok(new { order = updated });
    }

    private static object[] ToSelectionOptions(IReadOnlyList<OrderItemSnapshot> items) =>
        items.Take(5)
            .Select(item => new { order_item_id = item.OrderItemId, product_name = item.ProductName, quantity = item.Quantity })
            .Cast<object>()
            .ToArray();

    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}
