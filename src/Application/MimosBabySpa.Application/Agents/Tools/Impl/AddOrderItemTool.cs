using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
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
        "Adds a catalog product and an explicit additional customer-provided quantity to the current conversation order draft. " +
        "Do not infer quantity or default to 1; if the customer selected a product without saying how many units, ask for quantity first. " +
        "Use update_order_item_quantity, not this tool, when the customer wants to change an existing cart item to an exact total quantity. " +
        "Use the product_id returned by search_products when available.";
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
        if (productId.HasValue && !WasProductIdReturnedByLastSearch(ctx, productId.Value))
        {
            productId = null;
            rawProductId = null;
        }
        IReadOnlyList<ProductCandidate> ambiguous = [];
        if (productId is null
            && TryResolveRememberedProduct(ctx, rawProductId, externalId, sku, name, quantity, out var remembered, out ambiguous))
        {
            productId = remembered.ProductId;
            externalId = remembered.ExternalProductId;
            sku = remembered.Sku;
            name = remembered.Name;
            unitPrice ??= remembered.UnitPrice;
        }
        else if (productId is null && ambiguous.Count > 0)
        {
            return ToolResultHelper.Error(
                "product_ambiguous",
                "The product selection is ambiguous.",
                ProductSelectionMemory.BuildClarificationHint(ambiguous),
                recoverable: true);
        }

        if (productId is null && string.IsNullOrWhiteSpace(name) && IsMeaningfulProductText(rawProductId))
            name = rawProductId!.Replace('-', ' ');

        if (productId is null
            && string.IsNullOrWhiteSpace(externalId)
            && string.IsNullOrWhiteSpace(sku)
            && string.IsNullOrWhiteSpace(name))
        {
            return ToolResultHelper.MissingPrerequisites(["product_id"]);
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
            var hint = ProductSelectionMemory.TryGetLastSearch(ctx, out var lastSearch)
                ? ProductSelectionMemory.BuildClarificationHint(lastSearch.Products)
                : "Use the product_id, external_product_id, or sku returned by search_products. If missing, call search_products again.";

            return ToolResultHelper.Error(
                "product_not_found",
                "The product could not be resolved in the catalog.",
                hint,
                recoverable: true);
        }

        return ToolResultHelper.Ok(new { order = draft });
    }

    private static bool TryResolveRememberedProduct(
        AgentToolContext ctx,
        string? rawProductId,
        string? externalId,
        string? sku,
        string? name,
        decimal quantity,
        out ProductCandidate candidate,
        out IReadOnlyList<ProductCandidate> ambiguous)
    {
        candidate = default!;
        ambiguous = [];

        if (ProductSelectionMemory.TryGetSelected(ctx, out var selected))
        {
            candidate = selected;
            return true;
        }

        var allowIndex = IsExplicitIndexSelection(rawProductId, ctx.LatestUserMessage);
        var selector = allowIndex ? rawProductId : FirstMeaningfulSelector(rawProductId, externalId, sku, name, ctx.LatestUserMessage);
        if (ProductSelectionMemory.TryResolveFromLastSearch(ctx, selector, allowIndex, quantity, out var resolved, out var matches))
        {
            candidate = resolved;
            return true;
        }

        if (matches.Count > 1)
        {
            ambiguous = matches;
            return false;
        }

        if (IsQuantityOnly(ctx.LatestUserMessage, quantity)
            && ProductSelectionMemory.TryGetLastSearch(ctx, out var lastSearch))
        {
            if (lastSearch.Products.Count > 1)
            {
                ambiguous = lastSearch.Products;
            }
        }

        return false;
    }

    private static bool WasProductIdReturnedByLastSearch(AgentToolContext ctx, Guid productId)
    {
        if (!ProductSelectionMemory.TryGetLastSearch(ctx, out var lastSearch))
            return true;

        return lastSearch.Products.Any(candidate => candidate.ProductId == productId);
    }

    private static string? FirstMeaningfulSelector(params string?[] values) =>
        values.FirstOrDefault(IsMeaningfulProductText);

    private static bool IsMeaningfulProductText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (decimal.TryParse(trimmed, out _))
            return false;

        return true;
    }

    private static bool IsExplicitIndexSelection(string? rawProductId, string? latestUserMessage)
    {
        if (string.IsNullOrWhiteSpace(rawProductId)
            || !int.TryParse(rawProductId.Trim(), out _)
            || string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return false;
        }

        var normalized = CatalogSearchText.NormalizeCompact(latestUserMessage);
        return normalized.Contains("opcion")
               || normalized.Contains("numero")
               || latestUserMessage.Contains("#", StringComparison.Ordinal);
    }
    private static bool IsQuantityOnly(string? latestUserMessage, decimal quantity) =>
        !string.IsNullOrWhiteSpace(latestUserMessage)
        && decimal.TryParse(latestUserMessage.Trim(), out var parsed)
        && parsed == quantity;


    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}
