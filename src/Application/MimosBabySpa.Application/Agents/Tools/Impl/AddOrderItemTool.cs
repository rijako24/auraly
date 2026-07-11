using System.Globalization;
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

        if (IsShortAffirmation(ctx.LatestUserMessage))
        {
            var currentDraft = await _commerce.GetDraftAsync(ctx, cancellationToken);
            var existingItem = FindExistingItem(currentDraft, productId, externalId, sku, name);
            if (existingItem is not null)
            {
                return ToolResultHelper.ErrorWithLlm(
                    "duplicate_add_from_short_confirmation",
                    "The latest customer message is only a short confirmation and the product is already in the cart. Do not add it again unless the customer explicitly asks for more units.",
                    new
                    {
                        next_action = "keep_existing_cart_item",
                        product_name = existingItem.ProductName,
                        existing_quantity = existingItem.Quantity,
                        requested_additional_quantity = quantity,
                        customer_prompt_guidance = "Acknowledge the existing cart item and continue with the pending confirmation or next missing order data. If the customer wants more units, ask them to state the additional quantity explicitly."
                    },
                    recoverable: true);
            }
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
        catch (InsufficientProductStockException ex)
        {
            return ToolResultHelper.ErrorWithLlm(
                "insufficient_product_stock",
                "The requested quantity is greater than the available catalog stock. Do not add the item automatically; ask the customer whether they want the available quantity instead.",
                new
                {
                    next_action = "confirm_available_quantity_before_adding",
                    product_name = ex.ProductName,
                    requested_quantity = ex.RequestedQuantity,
                    available_quantity = ex.AvailableQuantity,
                    existing_cart_quantity = ex.ExistingCartQuantity,
                    available_to_add = ex.AvailableToAdd,
                    customer_prompt_guidance = "Tell the customer only the available quantity for this requested product and ask if they want to include that available quantity instead. Do not mention SKU/code or internal stock metadata."
                },
                recoverable: true);
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

    private static OrderItemSnapshot? FindExistingItem(
        OrderSnapshot draft,
        Guid? productId,
        string? externalId,
        string? sku,
        string? name)
    {
        if (draft.Items.Count == 0)
            return null;

        return draft.Items.FirstOrDefault(item =>
            (productId.HasValue && item.ProductId == productId)
            || (!string.IsNullOrWhiteSpace(externalId)
                && item.ExternalProductId?.Equals(externalId, StringComparison.OrdinalIgnoreCase) == true)
            || (!string.IsNullOrWhiteSpace(sku)
                && item.Sku?.Equals(sku, StringComparison.OrdinalIgnoreCase) == true)
            || (!string.IsNullOrWhiteSpace(name)
                && NormalizeProductText(item.ProductName) == NormalizeProductText(name)));
    }

    private static bool IsShortAffirmation(string? message)
    {
        var normalized = NormalizeIntentText(message);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized is "si" or "ok" or "okay" or "dale" or "correcto" or "confirmo" or "listo" or "de acuerdo";
    }

    private static string NormalizeIntentText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Normalize(System.Text.NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeProductText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }
    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = 0;
        return args.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }
}
