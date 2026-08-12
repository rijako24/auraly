using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class SearchProductsOperation : IAgentOperation
{
    private readonly ICommerceService _commerce;
    private readonly IConversationFactsService? _factsService;
    private readonly ICatalogRecommendationService? _recommendations;

    public SearchProductsOperation(ICommerceService commerce) => _commerce = commerce;

    public SearchProductsOperation(ICommerceService commerce, IConversationFactsService factsService)
    {
        _commerce = commerce;
        _factsService = factsService;
    }

    public SearchProductsOperation(
        ICommerceService commerce,
        IConversationFactsService factsService,
        ICatalogRecommendationService recommendations)
    {
        _commerce = commerce;
        _factsService = factsService;
        _recommendations = recommendations;
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
            "mode": { "description": "Semantic catalog intent: explore the full catalog, search concrete targets, or continue the active result set.", "type": "string", "enum": ["explore_catalog", "search_target", "continue_results"] },
            "limit": { "type": "integer", "minimum": 1, "maximum": 50 },
            "page": { "type": "integer", "minimum": 1 },
            "include_stock": { "type": "boolean" },
            "replacement_reference": { "description": "Optional original cart reference that this catalog query intends to replace.", "type": ["string", "null"] }
          }
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.search_products", InputSchema,
        ["categories.found", "categories.not_found", "products.found", "products.not_found", "catalog.no_more", "catalog.not_ready", "products.search_failed"],
        [], [], []);

    public Task<OperationOutcome> ExecuteAsync(
        JsonElement arguments,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(arguments, context, preserveSavedQueries: false, cancellationToken);

    private async Task<OperationOutcome> ExecuteAsync(
        JsonElement arguments,
        OperationContext context,
        bool preserveSavedQueries,
        CancellationToken cancellationToken)
    {
        var ctx = context.Session
            ?? throw new InvalidOperationException("commerce.search_products requires a conversation session.");
        var mode = ReadMode(arguments);
        if (mode == ProductCatalogQueryMode.ExploreCatalog)
            return await ExecuteCategoriesAsync(ctx, arguments, cancellationToken);
        if (mode == ProductCatalogQueryMode.ContinueResults)
            return await ExecuteContinuationAsync(ctx, context, cancellationToken);

        var query = OperationJsonHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var category = OperationJsonHelper.TryGetString(arguments, "category", out var c) ? c : null;
        var family = OperationJsonHelper.TryGetString(arguments, "family", out var f) ? f : null;
        var subcategory = OperationJsonHelper.TryGetString(arguments, "subcategory", out var sc) ? sc : null;
        var productClass = OperationJsonHelper.TryGetString(arguments, "product_class", out var pc) ? pc : null;
        var limit = OperationJsonHelper.TryGetInt(arguments, "limit", out var l) ? l : 10;
        var page = OperationJsonHelper.TryGetInt(arguments, "page", out var p) ? p : 1;
        var includeStock = !OperationJsonHelper.TryGetBool(arguments, "include_stock", out var s) || s;
        var replacementReference = OperationJsonHelper.TryGetString(arguments, "replacement_reference", out var rr)
            ? rr
            : null;
        var requestedQueries = ReadQueries(arguments, query);
        var queries = preserveSavedQueries
            ? requestedQueries
            : GroundQueriesInRecentOffers(ctx, requestedQueries);
        if (queries.Count == 1
            && string.IsNullOrWhiteSpace(category)
            && string.IsNullOrWhiteSpace(family)
            && string.IsNullOrWhiteSpace(subcategory)
            && string.IsNullOrWhiteSpace(productClass))
        {
            var resolvedCategory = await _commerce.ResolveCategoryNameAsync(
                ctx, queries[0], cancellationToken);
            if (resolvedCategory is not null)
            {
                category = resolvedCategory;
                query = null;
                queries = [];
            }
        }
        if (queries.Count == 0
            && string.IsNullOrWhiteSpace(category)
            && string.IsNullOrWhiteSpace(family)
            && string.IsNullOrWhiteSpace(subcategory)
            && string.IsNullOrWhiteSpace(productClass))
            return OperationOutcome.Fail(
                "products.search_failed", "Search mode requires a concrete local catalog criterion.", true);

        var request = new ProductSearchRequest(
            query, category, limit, includeStock, family, subcategory, productClass, page, mode);
        var execution = await ExecuteQueriesAsync(ctx, queries, request, null, cancellationToken);
        if (TryGetForegroundContextAnchor(ctx, out var contextAnchor))
        {
            var contextualQueries = ContextualizeQueries(contextAnchor, queries);
            if (!contextualQueries.SequenceEqual(queries, StringComparer.OrdinalIgnoreCase))
            {
                var contextualExecution = await ExecuteQueriesAsync(
                    ctx, contextualQueries, request, contextAnchor, cancellationToken);
                contextualExecution = contextualExecution with
                {
                    Result = FilterSemanticallyRelevantProducts(
                        contextualExecution.Result, queries, ctx.Config?.Commerce.Matching)
                };
                if (contextualExecution.Result.Products.Count > 0)
                {
                    execution = contextualExecution;
                }
                else
                {
                    var anchoredPrimary = FilterSemanticallyRelevantProducts(
                        execution.Result,
                        contextAnchor,
                        ctx.Config?.Commerce.Matching);
                    if (anchoredPrimary.Products.Count > 0)
                        execution = execution with
                        {
                            Result = anchoredPrimary,
                            SearchTerms = contextualQueries
                        };
                }
            }
        }
        var result = execution.Result;
        if (!result.CatalogReady)
        {
            return OperationOutcome.Ok("catalog.not_ready", new
            {
                count = 0,
                response_guidance = "State that the synchronized local catalog is not ready yet. Do not claim that the product does not exist and do not search the external provider."
            });
        }

        if (_factsService is not null)
        {
            await CatalogQueryMemory.RememberProductsAsync(
                _factsService,
                ctx,
                request,
                execution.SearchTerms,
                result.HasMore,
                replacementReference,
                cancellationToken);
        }

        var previouslyRecommended = CatalogRecommendationMemory.Read(ctx.Facts)?.Products
            .Select(product => product.ToProductReference())
            .ToList() ?? [];
        var recommendation = _recommendations is null || result.Products.Count == 0
            ? null
            : await _recommendations.ResolveAsync(ctx, result.Products, previouslyRecommended, cancellationToken);

        if (_factsService is not null && result.Products.Count > 0)
        {
            await ProductSelectionMemory.RememberCatalogAsync(
                _factsService, ctx, result.Products, execution.SearchTerms, cancellationToken, replacementReference);
            if (recommendation is not null)
                await CatalogRecommendationMemory.RememberAsync(_factsService, ctx, recommendation.Product, cancellationToken);
        }

        var recommendationItems = recommendation is null
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    name = recommendation.Product.Name,
                    description = recommendation.Product.Description,
                    category = recommendation.Product.CategoryName,
                    unit_price = recommendation.Product.EffectiveUnitPrice ?? recommendation.Product.UnitPrice,
                    currency = recommendation.Product.Currency,
                    promotion_name = recommendation.Product.PromotionName,
                    promotion_summary = recommendation.Product.PromotionSummary,
                    relation_type = recommendation.Type.ToString(),
                    reason = recommendation.Reason
                }
            };

        var outcomeCode = result.Products.Count == 0 ? "products.not_found" : "products.found";
        return OperationOutcome.Ok(outcomeCode, new
        {
            source = result.Source,
            count = result.Products.Count,
            search_terms = execution.SearchTerms,
            original_search_terms = requestedQueries,
            matched_search_terms = execution.MatchedTerms,
            unmatched_search_terms = execution.UnmatchedTerms,
            search_text = string.Join(", ", execution.SearchTerms),
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
            recommendations = recommendationItems,
            result.HasMore,
            applied_filters = result.AppliedFilters ?? ProductSearchAppliedFilters.From(request),
            clarification_candidates = Array.Empty<object>(),
            resolution_hint = (string?)null,
            selection_status = "catalog_results",
            response_guidance = "Present products as the main catalog results and keep recommendations in a separate, clearly optional section. Present at most the single returned recommendation; never merge it into the main options or invent another one. If unmatched_search_terms contains values, state briefly that those terms had no related catalog result; never substitute unrelated products for them. Show product name/presentation plus unit_price with currency when available. Do not show SKU/code, external ids, raw catalog ids, or stock quantities/status in normal product options. Use SKU/code only internally for operation calls. Mention available stock quantity only when a requested quantity is greater than the returned available stock, then ask whether the customer wants the available quantity instead. Do not omit returned prices for sellable product options, and do not invent missing prices."
        });
    }
    private async Task<OperationOutcome> ExecuteCategoriesAsync(
        AgentConversationContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var page = OperationJsonHelper.TryGetInt(arguments, "page", out var requestedPage)
            ? Math.Max(requestedPage, 1)
            : 1;
        var pageSize = OperationJsonHelper.TryGetInt(arguments, "limit", out var requestedLimit)
            ? Math.Clamp(requestedLimit, 1, 50)
            : 10;
        var result = await _commerce.BrowseCategoriesAsync(context, page, pageSize, cancellationToken);
        if (!result.CatalogReady)
        {
            return OperationOutcome.Ok("catalog.not_ready", new
            {
                count = 0,
                response_guidance = "State that the synchronized local catalog is not ready yet. Do not invent categories and do not search the external provider."
            });
        }

        if (_factsService is not null)
        {
            await CatalogQueryMemory.RememberCategoriesAsync(
                _factsService,
                context,
                result.Page,
                result.PageSize,
                result.HasMore,
                cancellationToken);
        }

        var outcomeCode = result.Categories.Count == 0
            ? "categories.not_found"
            : "categories.found";
        return OperationOutcome.Ok(outcomeCode, new
        {
            count = result.Categories.Count,
            categories = result.Categories.Select(category => new
            {
                name = category.Name
            }).ToList(),
            page = result.Page,
            has_more = result.HasMore,
            selection_status = "catalog_categories",
            response_guidance = "Present the returned categories as the official catalog navigation. Do not invent products, prices, category names or category counts. Invite the customer to choose one category or name a specific product they need. If has_more is true, mention briefly that more categories are available."
        });
    }

    private async Task<OperationOutcome> ExecuteContinuationAsync(
        AgentConversationContext session,
        OperationContext operationContext,
        CancellationToken cancellationToken)
    {
        var cursor = CatalogQueryMemory.Read(session.Facts);
        if (cursor is null || !cursor.HasMore)
        {
            return OperationOutcome.Ok("catalog.no_more", new
            {
                count = 0,
                has_more = false,
                response_guidance = "State briefly that there are no more options in the previous catalog result set. Invite the customer to choose a shown category/product or start a new specific search."
            });
        }

        if (cursor.Kind == CatalogQueryCursorKind.Categories)
        {
            var categoryArguments = JsonSerializer.SerializeToElement(new
            {
                mode = "explore_catalog",
                page = cursor.NextPage,
                limit = cursor.PageSize
            });
            return await ExecuteCategoriesAsync(session, categoryArguments, cancellationToken);
        }

        var productArguments = JsonSerializer.SerializeToElement(new
        {
            mode = "search_target",
            queries = cursor.Queries,
            category = cursor.Category,
            family = cursor.Family,
            subcategory = cursor.Subcategory,
            product_class = cursor.ProductClass,
            page = cursor.NextPage,
            limit = cursor.PageSize,
            include_stock = cursor.IncludeStock,
            replacement_reference = cursor.ReplacementReference
        });
        return await ExecuteAsync(productArguments, operationContext, preserveSavedQueries: true, cancellationToken);
    }

    private static ProductCatalogQueryMode ReadMode(JsonElement arguments)
    {
        if (!OperationJsonHelper.TryGetString(arguments, "mode", out var rawMode))
            throw new InvalidOperationException("commerce.search_products requires an explicit catalog mode.");
        return rawMode.Trim().ToLowerInvariant() switch
        {
            "explore_catalog" => ProductCatalogQueryMode.ExploreCatalog,
            "search_target" => ProductCatalogQueryMode.SearchTarget,
            "continue_results" => ProductCatalogQueryMode.ContinueResults,
            _ => throw new InvalidOperationException($"Unsupported catalog mode '{rawMode}'.")
        };
    }

    private async Task<SearchExecution> ExecuteQueriesAsync(
        AgentConversationContext context,
        IReadOnlyList<string> queries,
        ProductSearchRequest request,
        string? relevanceQuery,
        CancellationToken cancellationToken) =>
        queries.Count switch
        {
            0 => SearchExecution.FromSingle(
                await SearchSingleAsync(context, request, relevanceQuery, cancellationToken),
                queries),
            1 => SearchExecution.FromSingle(
                await SearchSingleAsync(
                    context,
                    request with { Query = queries[0] },
                    relevanceQuery,
                    cancellationToken),
                queries),
            _ => await SearchManyAsync(
                context, queries, request, relevanceQuery, cancellationToken)
        };


    private async Task<ProductSearchResult> SearchSingleAsync(
        AgentConversationContext ctx,
        ProductSearchRequest request,
        string? relevanceQuery,
        CancellationToken cancellationToken)
    {
        var result = await _commerce.SearchProductsAsync(ctx, request, cancellationToken);

        result = FilterSemanticallyRelevantProducts(result, relevanceQuery ?? request.Query, ctx.Config?.Commerce.Matching);
        return PreferExactNameMatches(result, request.Query, ctx.Config?.Commerce.Matching);
    }

    private static ProductSearchResult FilterSemanticallyRelevantProducts(
        ProductSearchResult result,
        string? query,
        ProductMatchingPolicy? matchingPolicy)
    {
        if (string.IsNullOrWhiteSpace(query))
            return result;

        return FilterSemanticallyRelevantProducts(result, [query], matchingPolicy);
    }

    private static ProductSearchResult FilterSemanticallyRelevantProducts(
        ProductSearchResult result,
        IReadOnlyList<string> queries,
        ProductMatchingPolicy? matchingPolicy)
    {
        var activeQueries = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .ToList();
        if (activeQueries.Count == 0 || result.Products.Count == 0)
            return result;

        var relevant = result.Products
            .Where(product => activeQueries.Any(query =>
                ProductResolutionEngine.IsRelevantCatalogCandidate(
                    query,
                    product,
                    matchingPolicy)))
            .ToList();
        return relevant.Count == result.Products.Count
            ? result
            : result with { Products = relevant };
    }

    private static ProductSearchResult PreferExactNameMatches(
        ProductSearchResult result,
        string? query,
        ProductMatchingPolicy? matchingPolicy)
    {
        var minimumMatches = matchingPolicy?.ExactNameDominanceMinimumMatches ?? 0;
        var queryTokens = ProductSearchText.GetMatchingTokens(query);
        if (queryTokens.Count == 0 || result.Products.Count == 0)
            return result;

        var exactMatches = result.Products
            .Where(product =>
            {
                var nameTokens = ProductSearchText.GetMatchingTokens(product.Name);
                return queryTokens.All(nameTokens.Contains);
            })
            .ToList();
        if (exactMatches.Count == 0 || exactMatches.Count == result.Products.Count)
            return result;

        var leadingMatches = queryTokens.Count == 1
            ? exactMatches.Where(product =>
                ProductSearchText.GetMatchingTokens(product.Name).FirstOrDefault()
                    ?.Equals(queryTokens[0], StringComparison.Ordinal) == true).ToList()
            : [];
        if (leadingMatches.Count > 0)
            return result with { Products = leadingMatches };

        var shouldDominate = queryTokens.Count > 1
            || minimumMatches > 0 && exactMatches.Count >= minimumMatches;
        return shouldDominate ? result with { Products = exactMatches } : result;
    }

    private async Task<SearchExecution> SearchManyAsync(
        AgentConversationContext ctx,
        IReadOnlyList<string> queries,
        ProductSearchRequest template,
        string? relevanceQuery,
        CancellationToken cancellationToken)
    {
        var batches = new List<(string Term, ProductSearchResult Result)>();
        foreach (var term in queries)
        {
            var result = await SearchSingleAsync(
                ctx,
                template with { Query = term, Category = null },
                relevanceQuery,
                cancellationToken);
            batches.Add((term, result));
        }

        var products = new List<ProductReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(template.Limit, 1, 50);
        var maximumDepth = batches.Select(batch => batch.Result.Products.Count)
            .DefaultIfEmpty(0)
            .Max();
        for (var rank = 0; rank < maximumDepth && products.Count < limit; rank++)
        {
            foreach (var batch in batches)
            {
                if (rank >= batch.Result.Products.Count)
                    continue;

                var product = batch.Result.Products[rank];
                if (seen.Add(ProductResultKey(product)))
                    products.Add(product);
                if (products.Count >= limit)
                    break;
            }
        }

        var source = batches.Select(batch => batch.Result.Source)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "catalog";
        var hasMore = batches.Any(batch => batch.Result.HasMore)
            || batches.Sum(batch => batch.Result.Products.Count) > products.Count;
        var merged = new ProductSearchResult(
            products,
            source,
            hasMore,
            ProductSearchAppliedFilters.From(template));
        return new SearchExecution(
            merged,
            queries,
            batches.Where(batch => batch.Result.Products.Count > 0)
                .Select(batch => batch.Term).ToList(),
            batches.Where(batch => batch.Result.Products.Count == 0)
                .Select(batch => batch.Term).ToList());
    }

    private static string ProductResultKey(ProductReference product) =>
        product.ProductId?.ToString("N")
        ?? product.ExternalProductId
        ?? product.Sku
        ?? product.Name;
    private static IReadOnlyList<string> GroundQueriesInRecentOffers(
        AgentConversationContext context,
        IReadOnlyList<string> queries)
    {
        var memory = CatalogOfferMemory.Read(context.Facts);
        if (memory is null || queries.Count == 0)
            return queries;

        var candidates = CatalogOfferMemory.AllProducts(memory)
            .Select(product => new RetrievedProductCandidate(
                product.ToProductReference(),
                ProductMatchSource.RememberedCatalog))
            .ToList();
        return queries.Select(query =>
        {
            var resolution = ProductResolutionEngine.Resolve(query, candidates);
            if (resolution.Status == ProductResolutionStatus.Resolved
                && resolution.Selected is not null)
                return resolution.Selected.Name;
            if (resolution.Status == ProductResolutionStatus.SuggestionRequired
                && resolution.Candidates.Count == 1)
                return resolution.Candidates[0].Product.Name;
            return query;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetForegroundContextAnchor(
        AgentConversationContext context,
        out string anchor)
    {
        anchor = string.Empty;
        var memory = CatalogOfferMemory.Read(context.Facts);
        if (memory is null
            || !CatalogOfferMemory.IsLatestOfferForeground(
                memory,
                context.ConversationState?.LastBotMessage,
                context.Config?.Commerce.Matching.CatalogCandidateMinimumCoverage ?? 0.7d))
            return false;

        var latest = memory.Snapshots.MaxBy(snapshot => snapshot.Sequence);
        var anchorTerms = latest?.ContextAnchorTerms is { Count: > 0 }
            ? latest.ContextAnchorTerms
            : latest?.SearchTerms;
        if (anchorTerms is not { Count: 1 }
            || string.IsNullOrWhiteSpace(anchorTerms[0]))
            return false;

        anchor = anchorTerms[0].Trim();
        return true;
    }

    private static IReadOnlyList<string> ContextualizeQueries(
        string anchor,
        IReadOnlyList<string> queries)
    {
        if (queries.Count == 0)
            return queries;

        var anchorTokens = ProductSearchText.GetMatchingTokens(anchor);
        if (anchorTokens.Count == 0)
            return queries;

        return queries
            .Select(query =>
            {
                var queryTokens = ProductSearchText.GetMatchingTokens(query);
                return anchorTokens.All(queryTokens.Contains)
                    ? query
                    : $"{anchor} {query}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    private sealed record SearchExecution(
        ProductSearchResult Result,
        IReadOnlyList<string> SearchTerms,
        IReadOnlyList<string> MatchedTerms,
        IReadOnlyList<string> UnmatchedTerms)
    {
        public static SearchExecution FromSingle(
            ProductSearchResult result,
            IReadOnlyList<string> terms) =>
            new(
                result,
                terms,
                result.Products.Count > 0 ? terms : [],
                result.Products.Count == 0 ? terms : []);
    }



    private static IReadOnlyList<string> ReadQueries(JsonElement arguments, string? query)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
            values.Add(query.Trim());

        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("queries", out var queriesElement))
        {
            AddQueries(values, queriesElement);
        }

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
