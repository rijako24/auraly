using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class RemoveOrderItemTool : IAgentTool
{
    private readonly ICommerceService _commerce;
    private readonly IConversationFactsService _factsService;

    public RemoveOrderItemTool(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    public string Name => "remove_order_item";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderDraftUpdate];
    public string Description =>
        "Removes an item from the current conversation order draft, or reduces it when an explicit desired remaining quantity is provided. " +
        "Use update_order_item_quantity when the customer wants to set or increase an existing item to an exact total quantity. " +
        "Use order_item_id from get_order_draft when available; otherwise provide product_id, sku, name, or a clear product reference.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "order_item_id": { "type": "string" },
            "product_id": { "type": "string" },
            "external_product_id": { "type": "string" },
            "sku": { "type": "string" },
            "name": { "type": "string" },
            "quantity": {
              "type": "number",
              "description": "Desired remaining quantity. Use 0 or omit to remove the item completely."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var draft = await _commerce.GetDraftAsync(ctx, cancellationToken);
        if (draft.Items.Count == 0)
            return ToolResultHelper.Error("empty_order", "The order draft has no items.", "Help the customer choose a product first.", recoverable: true);

        if (!OrderItemSelectionResolver.TryResolve(arguments, draft, out var item, out var ambiguous))
        {
            if (ambiguous.Count > 1)
            {
                return ToolResultHelper.Error(
                    "order_item_ambiguous",
                    "The order item selection is ambiguous.",
                    BuildClarificationHint(ambiguous),
                    recoverable: true);
            }

            return ToolResultHelper.MissingPrerequisites(["order_item_id"]);
        }

        var desiredQuantity = TryGetDecimal(arguments, "quantity", out var explicitQuantity)
            ? explicitQuantity
            : (decimal?)null;

        OrderSnapshot updated;
        if (desiredQuantity.HasValue)
        {
            updated = await _commerce.UpdateItemQuantityAsync(ctx, item.OrderItemId, desiredQuantity.Value, cancellationToken);
        }
        else
        {
            updated = await _commerce.RemoveItemAsync(ctx, item.OrderItemId, cancellationToken);
        }

        await OrderDraftFactInvalidation.ClearOrderFinalizedAsync(_factsService, ctx, cancellationToken);

        return ToolResultHelper.Ok(new { order = updated });
    }

    private static string BuildClarificationHint(IReadOnlyList<OrderItemSnapshot> items)
    {
        var options = items.Take(5).Select(i => $"{i.OrderItemId}: {i.ProductName} x{i.Quantity}");
        return "Pregunta cual item del pedido quiere modificar: " + string.Join("; ", options) + ".";
    }

    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}
