using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

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
        var effectiveQuery = query;
        var result = await _commerce.SearchProductsAsync(ctx, new ProductSearchRequest(query, category, limit, includeStock), cancellationToken);
        if (result.Products.Count == 0 && string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(category))
        {
            var fallbackQuery = category.Trim();
            result = await _commerce.SearchProductsAsync(
                ctx,
                new ProductSearchRequest(fallbackQuery, null, limit, includeStock),
                cancellationToken);
            effectiveQuery = fallbackQuery;
        }

        if (result.Products.Count == 0 && IsBroadOptionsRequest(ctx.LatestUserMessage))
        {
            result = await _commerce.SearchProductsAsync(
                ctx,
                new ProductSearchRequest(null, null, limit, includeStock),
                cancellationToken);
            effectiveQuery = null;
        }
        ProductCandidate? selected;
        IReadOnlyList<ProductCandidate> clarificationCandidates = [];
        string? resolutionHint = null;

        if (result.Products.Count == 0
            && ProductSelectionMemory.TryGetLastSearch(ctx, out _))
        {
            if (ProductSelectionMemory.TryResolveFromLastSearch(
                    ctx,
                    ctx.LatestUserMessage,
                    allowIndex: false,
                    quantityToIgnore: null,
                    out var previousSelection,
                    out var previousMatches))
            {
                selected = previousSelection;
                await ProductSelectionMemory.RememberSelectedAsync(_factsService, ctx, selected, cancellationToken);
                resolutionHint = "resolved_from_previous_search";
            }
            else
            {
                selected = null;
                clarificationCandidates = previousMatches;
                resolutionHint = clarificationCandidates.Count > 1
                    ? ProductSelectionMemory.BuildClarificationHint(clarificationCandidates)
                    : "not_found";

                await ProductSelectionMemory.ClearSelectedAsync(_factsService, ctx, cancellationToken);
            }
        }
        else
        {
            selected = await ProductSelectionMemory.RememberSearchAsync(_factsService, ctx, effectiveQuery, result, cancellationToken);
        }

        return ToolResultHelper.Ok(new
        {
            source = result.Source,
            count = result.Products.Count,
            products = result.Products,
            result.HasMore,
            selected_product = selected,
            clarification_candidates = clarificationCandidates,
            resolution_hint = resolutionHint,
            selection_status = selected is null ? "ambiguous_or_not_selected" : "inferred"
        });
    }

    private static bool IsBroadOptionsRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var normalized = CatalogSearchText.NormalizeCompact(message);
        string[] triggers =
        [
            "opciones",
            "todas",
            "todos",
            "catalogo",
            "catalogue",
            "lista",
            "listar",
            "mostrar",
            "muestrame",
            "ver"
        ];

        return triggers.Any(normalized.Contains);
    }
}


