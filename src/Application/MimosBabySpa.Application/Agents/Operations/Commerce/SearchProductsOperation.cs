using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class SearchProductsOperation : IAgentOperation
{
    private readonly ICommerceService _commerce;
    private readonly IConversationFactsService? _factsService;

    public SearchProductsOperation(ICommerceService commerce) => _commerce = commerce;

    public SearchProductsOperation(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    private const string InputSchema = """
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

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.search_products", InputSchema,
        ["products.found", "products.not_found", "products.search_failed"],
        [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement arguments,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var ctx = context.Session
            ?? throw new InvalidOperationException("commerce.search_products requires a conversation session.");
        var query = OperationJsonHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var category = OperationJsonHelper.TryGetString(arguments, "category", out var c) ? c : null;
        var family = OperationJsonHelper.TryGetString(arguments, "family", out var f) ? f : null;
        var subcategory = OperationJsonHelper.TryGetString(arguments, "subcategory", out var sc) ? sc : null;
        var productClass = OperationJsonHelper.TryGetString(arguments, "product_class", out var pc) ? pc : null;
        var limit = OperationJsonHelper.TryGetInt(arguments, "limit", out var l) ? l : 10;
        var page = OperationJsonHelper.TryGetInt(arguments, "page", out var p) ? p : 1;
        var includeStock = !OperationJsonHelper.TryGetBool(arguments, "include_stock", out var s) || s;
        var queries = ReadQueries(arguments, query);
        var request = new ProductSearchRequest(query, category, limit, includeStock, family, subcategory, productClass, page);
        var result = queries.Count switch
        {
            0 => await SearchSingleAsync(ctx, request, cancellationToken),
            1 => await SearchSingleAsync(ctx, request with { Query = queries[0] }, cancellationToken),
            _ => await SearchManyAsync(ctx, queries, request, cancellationToken)
        };

        if (_factsService is not null && result.Products.Count > 0)
            await ProductSelectionMemory.RememberCatalogAsync(_factsService, ctx, result.Products, cancellationToken);

        var outcomeCode = result.Products.Count == 0 ? "products.not_found" : "products.found";
        return OperationOutcome.Ok(outcomeCode, new
        {
            source = result.Source,
            count = result.Products.Count,
            search_terms = queries,
            search_text = string.Join(", ", queries),
            products = result.Products.Select(product => new
            {
                name = product.Name,
                description = product.Description,
                category = product.CategoryName,
                unit_price = product.EffectiveUnitPrice ?? product.UnitPrice,
                currency = product.Currency,
                promotion_name = product.PromotionName,
                promotion_summary = product.PromotionSummary
            }).ToList(),
            result.HasMore,
            applied_filters = result.AppliedFilters ?? ProductSearchAppliedFilters.From(request),
            clarification_candidates = Array.Empty<object>(),
            resolution_hint = (string?)null,
            selection_status = "catalog_results",
            response_guidance = "When presenting product results to the customer, use only returned products and show product name/presentation plus unit_price with currency when available. Do not show SKU/code, external ids, raw catalog ids, or stock quantities/status in normal product options. Use SKU/code only internally for operation calls. Mention available stock quantity only when a requested quantity is greater than the returned available stock, then ask whether the customer wants the available quantity instead. Do not omit returned prices for sellable product options, and do not invent missing prices."
        });
    }
    private async Task<ProductSearchResult> SearchSingleAsync(AgentConversationContext ctx, ProductSearchRequest request, CancellationToken cancellationToken)
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
        AgentConversationContext ctx,
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
