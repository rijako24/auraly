using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class AddOrderItemTool : IAgentTool
{
    private readonly ICommerceService _commerce;
    private readonly IConversationFactsService _factsService;

    public AddOrderItemTool(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    public string Name => "add_order_item";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderDraftUpdate];
    public string Description =>
        "Adds a catalog product and quantity to the current conversation order draft. " +
        "Use the product_id returned by search_products.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "product_id": {
              "type": "string",
              "description": "Local catalog product_id returned by search_products."
            },
            "external_product_id": {
              "type": "string",
              "description": "External commerce product id returned by search_products."
            },
            "sku": {
              "type": "string",
              "description": "Catalog SKU returned by search_products."
            },
            "name": {
              "type": "string",
              "description": "Display name from search_products. Prefer product_id for catalog products."
            },
            "quantity": { "type": "number" },
            "unit_price": { "type": "number" }
          },
          "required": ["quantity"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        Guid? productId = null;
        string? rawProductId = null;
        if (ToolResultHelper.TryGetString(arguments, "product_id", out var id))
        {
            rawProductId = id;
            if (Guid.TryParse(id, out var parsed))
                productId = parsed;
        }
        var externalId = ToolResultHelper.TryGetString(arguments, "external_product_id", out var ext) ? ext : null;
        var sku = ToolResultHelper.TryGetString(arguments, "sku", out var s) ? s : null;
        var name = ToolResultHelper.TryGetString(arguments, "name", out var n) ? n : null;
        if (productId is null && string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(rawProductId))
            name = rawProductId.Replace('-', ' ');
        if (!TryGetDecimal(arguments, "quantity", out var quantity))
            return ToolResultHelper.MissingPrerequisites(["quantity"]);
        if (productId is null
            && string.IsNullOrWhiteSpace(externalId)
            && string.IsNullOrWhiteSpace(sku)
            && string.IsNullOrWhiteSpace(name))
        {
            return ToolResultHelper.MissingPrerequisites(["product_id"]);
        }

        decimal? unitPrice = TryGetDecimal(arguments, "unit_price", out var price) ? price : null;

        OrderSnapshot draft;
        try
        {
            draft = await _commerce.AddItemAsync(
                ctx,
                new AddOrderItemRequest(productId, externalId, sku, name, quantity, unitPrice),
                cancellationToken);
            await ClearOrderFinalizedFactAsync(ctx, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Product not found", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Error(
                "product_not_found",
                "The product could not be resolved in the catalog.",
                "Use the product_id, external_product_id, or sku returned by search_products. If missing, call search_products again.",
                recoverable: true);
        }

        return ToolResultHelper.Ok(new { order = draft });
    }

    private async Task ClearOrderFinalizedFactAsync(AgentToolContext ctx, CancellationToken cancellationToken)
    {
        var cleared = await _factsService.ClearFieldsAsync(ctx.ConversationId, ["order_finalized"], cancellationToken);
        if (cleared.Count > 0)
            ctx.Facts.Remove("order_finalized");
    }

    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}
