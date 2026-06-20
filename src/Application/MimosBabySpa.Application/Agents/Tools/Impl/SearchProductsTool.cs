using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class SearchProductsTool : IAgentTool
{
    private readonly ICommerceService _commerce;

    public SearchProductsTool(ICommerceService commerce) => _commerce = commerce;

    public string Name => "search_products";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.ProductSearch];
    public string Description => "Searches the configured commerce catalog by product name, code, description, or category.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "category": { "type": "string" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 50 },
            "include_stock": { "type": "boolean" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var query = ToolResultHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var category = ToolResultHelper.TryGetString(arguments, "category", out var c) ? c : null;
        var limit = ToolResultHelper.TryGetInt(arguments, "limit", out var l) ? l : 10;
        var includeStock = !ToolResultHelper.TryGetBool(arguments, "include_stock", out var s) || s;
        var result = await _commerce.SearchProductsAsync(ctx, new ProductSearchRequest(query, category, limit, includeStock), cancellationToken);
        return ToolResultHelper.Ok(new { source = result.Source, count = result.Products.Count, products = result.Products, result.HasMore });
    }
}
