using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("add_order_item", Capabilities = new[] { ToolCapabilities.OrderDraftUpdate })]
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
    public IReadOnlyList<string> OperatingGroups => [ToolOperatingGroups.OrderIntake];
    public string Description =>
        "Adds a catalog product and an explicit additional customer-provided quantity to the current conversation order draft. " +
        "Do not infer quantity or default to 1; if the customer selected a product without saying how many units, ask for quantity first. " +
        "Use update_order_item_quantity, not this tool, when the customer wants to change an existing cart item to an exact total quantity. " +
        "Use the product_id returned by the current search_products result when available.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "product_id": {
              "type": "string",
              "description": "Local catalog product_id returned by the current search_products result."
            },
            "external_product_id": {
              "type": "string",
              "description": "External commerce product id returned by the current search_products result."
            },
            "sku": {
              "type": "string",
              "description": "Catalog SKU returned by the current search_products result."
            },
            "name": {
              "type": "string",
              "description": "Display name from the current search_products result. Prefer product_id for catalog products."
            },
            "quantity": { "type": "number", "description": "Explicit quantity stated by the customer. Do not infer or default to 1." },
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
        if (!TryGetDecimal(arguments, "quantity", out var quantity))
            return ToolResultHelper.MissingPrerequisites(["quantity"]);
        decimal? unitPrice = TryGetDecimal(arguments, "unit_price", out var price) ? price : null;
        if (productId is null
            && string.IsNullOrWhiteSpace(externalId)
            && string.IsNullOrWhiteSpace(sku)
            && string.IsNullOrWhiteSpace(name)
            && ProductSelectionMemory.TryGetSelected(ctx, out var selected))
        {
            productId = selected.ProductId;
            externalId = selected.ExternalProductId;
            sku = selected.Sku;
            name = selected.Name;
            unitPrice ??= selected.UnitPrice;
        }

        if (productId is null && string.IsNullOrWhiteSpace(name) && IsMeaningfulProductText(rawProductId))
            name = rawProductId!.Replace('-', ' ');

        if (productId is null
            && string.IsNullOrWhiteSpace(externalId)
            && string.IsNullOrWhiteSpace(sku)
            && string.IsNullOrWhiteSpace(name))
        {
            return ToolResultHelper.ErrorWithNextAction(
                "missing_prerequisites",
                "A current active product selection is required before adding an item.",
                "select_product",
                new { required_source = "active_catalog_result" },
                recoverable: true);
        }

        OrderSnapshot draft;
        try
        {
            draft = await _commerce.AddItemAsync(
                ctx,
                new AddOrderItemRequest(productId, externalId, sku, name, quantity, unitPrice),
                cancellationToken);
            await ProductSelectionMemory.ClearSelectedAsync(_factsService, ctx, cancellationToken);
            await OrderDraftFactInvalidation.ClearOrderFinalizedAsync(_factsService, ctx, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Product not found", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.ErrorWithNextAction(
                "product_not_found",
                "The product could not be resolved in the catalog.",
                "search_products",
                new { catalog_filter = "active_products" },
                recoverable: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Product inactive", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.ErrorWithNextAction(
                "product_inactive",
                "The selected product is inactive and cannot be added to the order.",
                "search_products",
                new { catalog_filter = "active_products" },
                recoverable: true);
        }

        return ToolResultHelper.Ok(new { order = draft });
    }

    private static bool IsMeaningfulProductText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (decimal.TryParse(trimmed, out _))
            return false;

        return true;
    }

    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}

