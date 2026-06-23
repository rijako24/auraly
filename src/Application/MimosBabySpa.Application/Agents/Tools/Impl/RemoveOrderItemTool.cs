using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
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
        "Removes or reduces an item from the current conversation order draft. " +
        "Use order_item_id from get_order_draft when available; otherwise provide product_id, sku, name, or a clear product reference.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "order_item_id": { "type": "string" },
            "product_id": { "type": "string" },
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

        if (!TryResolveItem(arguments, ctx, draft, out var item, out var ambiguous))
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
            : InferDesiredQuantity(ctx.LatestUserMessage, item.Quantity);

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

    private static bool TryResolveItem(
        JsonElement arguments,
        AgentToolContext ctx,
        OrderSnapshot draft,
        out OrderItemSnapshot item,
        out IReadOnlyList<OrderItemSnapshot> ambiguous)
    {
        item = default!;
        ambiguous = [];

        var rawOrderItemId = ToolResultHelper.TryGetString(arguments, "order_item_id", out var oid) ? oid : null;
        if (Guid.TryParse(rawOrderItemId, out var parsedOrderItemId))
        {
            var byId = draft.Items.FirstOrDefault(i => i.OrderItemId == parsedOrderItemId);
            if (byId is not null)
            {
                item = byId;
                return true;
            }
        }

        var productId = ToolResultHelper.TryGetString(arguments, "product_id", out var pid) ? pid : null;
        var sku = ToolResultHelper.TryGetString(arguments, "sku", out var s) ? s : null;
        var name = ToolResultHelper.TryGetString(arguments, "name", out var n) ? n : null;
        var selector = FirstMeaningfulSelector(rawOrderItemId, productId, sku, name, ctx.LatestUserMessage);
        if (string.IsNullOrWhiteSpace(selector))
            return false;

        var matches = FindMatches(draft.Items, selector).DistinctBy(i => i.OrderItemId).ToList();
        if (matches.Count == 1)
        {
            item = matches[0];
            return true;
        }

        ambiguous = matches;
        return false;
    }

    private static IEnumerable<OrderItemSnapshot> FindMatches(IReadOnlyList<OrderItemSnapshot> items, string selector)
    {
        if (Guid.TryParse(selector, out var parsedProductId))
        {
            foreach (var item in items.Where(i => i.ProductId == parsedProductId))
                yield return item;
            yield break;
        }

        foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.Sku)
                                              && i.Sku.Equals(selector, StringComparison.OrdinalIgnoreCase)))
        {
            yield return item;
        }

        foreach (var item in items.Where(i => CatalogSearchText.NormalizeCompact(i.ProductName) == CatalogSearchText.NormalizeCompact(selector)))
        {
            yield return item;
        }

        foreach (var item in items.Where(i => CatalogSearchText.ContainsAllTerms(selector, i.ProductName, i.Sku)))
        {
            yield return item;
        }
    }

    private static string? FirstMeaningfulSelector(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && !decimal.TryParse(v.Trim(), out _));

    private static decimal? InferDesiredQuantity(string? latestUserMessage, decimal currentQuantity)
    {
        if (string.IsNullOrWhiteSpace(latestUserMessage))
            return null;

        var normalized = CatalogSearchText.NormalizeCompact(latestUserMessage);
        if (normalized.Contains("unaunidad") || normalized.Contains("unode") || normalized.Contains("unoyuna") || normalized.Contains("1unidad"))
            return 1;

        if (normalized.Contains("quitar") || normalized.Contains("eliminar") || normalized.Contains("sacar") || normalized.Contains("borrar"))
            return null;

        return currentQuantity > 1 ? currentQuantity - 1 : null;
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
