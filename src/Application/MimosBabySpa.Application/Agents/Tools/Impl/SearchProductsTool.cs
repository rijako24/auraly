using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("search_products", Capabilities = new[] { ToolCapabilities.ProductSearch })]
public sealed class SearchProductsTool : IAgentTool
{
    private readonly ICommerceService _commerce;
    private readonly IConversationFactsService _factsService;

    public SearchProductsTool(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    public string Name => "search_products";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.ProductSearch];
    public string Description => "Searches the configured commerce catalog by product name, code, description, category, family, subcategory, or product class. Use it for direct products and for related-product selling before mentioning sellable recommendations.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "queries": { "description": "Optional list of product/catalog queries to search and merge, useful when a recipe produced several ingredient terms.", "oneOf": [{ "type": "array", "items": { "type": "string" } }, { "type": "string" }] },
            "category": { "type": "string" },
            "family": { "type": "string" },
            "subcategory": { "type": "string" },
            "product_class": { "type": "string" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 50 },
            "page": { "type": "integer", "minimum": 1 },
            "include_stock": { "type": "boolean" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var query = ToolResultHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var category = ToolResultHelper.TryGetString(arguments, "category", out var c) ? c : null;
        var family = ToolResultHelper.TryGetString(arguments, "family", out var f) ? f : null;
        var subcategory = ToolResultHelper.TryGetString(arguments, "subcategory", out var sc) ? sc : null;
        var productClass = ToolResultHelper.TryGetString(arguments, "product_class", out var pc) ? pc : null;
        var limit = ToolResultHelper.TryGetInt(arguments, "limit", out var l) ? l : 10;
        var page = ToolResultHelper.TryGetInt(arguments, "page", out var p) ? p : 1;
        var includeStock = !ToolResultHelper.TryGetBool(arguments, "include_stock", out var s) || s;
        var queries = ReadQueries(arguments, query);
        var request = new ProductSearchRequest(query, category, limit, includeStock, family, subcategory, productClass, page);
        var result = queries.Count switch
        {
            0 => await SearchSingleAsync(ctx, request, cancellationToken),
            1 => await SearchSingleAsync(ctx, request with { Query = queries[0] }, cancellationToken),
            _ => await SearchManyAsync(ctx, queries, request, cancellationToken)
        };

        var selected = await ProductSelectionMemory.RememberSearchAsync(_factsService, ctx, result, cancellationToken);

        return ToolResultHelper.Ok(new
        {
            source = result.Source,
            count = result.Products.Count,
            products = result.Products,
            result.HasMore,
            applied_filters = result.AppliedFilters ?? ProductSearchAppliedFilters.From(request),
            selected_product = selected,
            clarification_candidates = Array.Empty<object>(),
            resolution_hint = (string?)null,
            selection_status = selected is null ? "ambiguous_or_not_selected" : "inferred",
            response_guidance = "When presenting product results to the customer, use only returned products and show product name/presentation plus unit_price with currency when available. Do not show SKU/code, external ids, raw catalog ids, or stock quantities/status in normal product options. Use SKU/code only internally for tool calls. Mention available stock quantity only when a requested quantity is greater than the returned available stock, then ask whether the customer wants the available quantity instead. Do not omit returned prices for sellable product options, and do not invent missing prices."
        });
    }
    private async Task<ProductSearchResult> SearchSingleAsync(AgentToolContext ctx, ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var result = await _commerce.SearchProductsAsync(ctx, request, cancellationToken);
        if (result.Products.Count == 0 && string.IsNullOrWhiteSpace(request.Query) && !string.IsNullOrWhiteSpace(request.Category))
        {
            var fallbackQuery = request.Category.Trim();
            result = await _commerce.SearchProductsAsync(
                ctx,
                request with { Query = fallbackQuery, Category = null },
                cancellationToken);
        }

        return result;
    }

    private async Task<ProductSearchResult> SearchManyAsync(
        AgentToolContext ctx,
        IReadOnlyList<string> queries,
        ProductSearchRequest template,
        CancellationToken cancellationToken)
    {
        var products = new List<ProductReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = string.Empty;
        var hasMore = false;
        foreach (var term in queries)
        {
            var result = await SearchSingleAsync(ctx, template with { Query = term, Category = null }, cancellationToken);
            if (string.IsNullOrWhiteSpace(source))
                source = result.Source;
            hasMore |= result.HasMore;

            foreach (var product in result.Products)
            {
                var key = ProductResultKey(product);
                if (seen.Add(key))
                    products.Add(product);

                if (products.Count >= Math.Clamp(template.Limit, 1, 50))
                    break;
            }

            if (products.Count >= Math.Clamp(template.Limit, 1, 50))
                break;
        }

        return new ProductSearchResult(
            products,
            string.IsNullOrWhiteSpace(source) ? "catalog" : source,
            hasMore,
            ProductSearchAppliedFilters.From(template));
    }

    private static string ProductResultKey(ProductReference product) =>
        product.ProductId?.ToString("N")
        ?? product.ExternalProductId
        ?? product.Sku
        ?? product.Name;

    private static IReadOnlyList<string> ReadQueries(JsonElement arguments, string? query)
    {
        var values = new List<string>();
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("queries", out var queriesElement))
        {
            AddQueries(values, queriesElement);
        }

        if (values.Count == 0 && !string.IsNullOrWhiteSpace(query))
            values.Add(query.Trim());

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static void AddQueries(List<string> values, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AddQueries(values, item);
            return;
        }

        if (element.ValueKind != JsonValueKind.String)
            return;

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                AddQueries(values, doc.RootElement);
                return;
            }
            catch (JsonException)
            {
            }
        }

        foreach (var part in trimmed.Split(['|', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            values.Add(part);
    }
}
