using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed record CatalogProductRecommendation(
    ProductReference Product,
    ProductRecommendationType Type,
    string? Reason);

public interface ICatalogRecommendationService
{
    Task<CatalogProductRecommendation?> ResolveAsync(
        AgentConversationContext context,
        IReadOnlyList<ProductReference> searchResults,
        IReadOnlyList<ProductReference> previouslyRecommended,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogRecommendationService : ICatalogRecommendationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductLookupService _productLookup;
    private readonly ILogger<CatalogRecommendationService> _logger;

    public CatalogRecommendationService(
        IUnitOfWork unitOfWork,
        IProductLookupService productLookup,
        ILogger<CatalogRecommendationService> logger)
    {
        _unitOfWork = unitOfWork;
        _productLookup = productLookup;
        _logger = logger;
    }

    public async Task<CatalogProductRecommendation?> ResolveAsync(
        AgentConversationContext context,
        IReadOnlyList<ProductReference> searchResults,
        IReadOnlyList<ProductReference> previouslyRecommended,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ResolveCoreAsync(
                context,
                searchResults,
                previouslyRecommended,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Catalog recommendations could not be resolved for business {BusinessId}; the primary catalog result remains valid.",
                context.BusinessId);
            return null;
        }
    }

    private async Task<CatalogProductRecommendation?> ResolveCoreAsync(
        AgentConversationContext context,
        IReadOnlyList<ProductReference> searchResults,
        IReadOnlyList<ProductReference> previouslyRecommended,
        CancellationToken cancellationToken = default)
    {
        if (searchResults.Count == 0)
            return null;

        var provider = context.Config?.Commerce.Enabled == true
            ? context.Config.Commerce.Provider
            : CommerceProvider.Local;
        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            context.BusinessId,
            provider,
            CommerceCapability.CatalogAndOrders,
            cancellationToken);
        var rules = await _unitOfWork.ProductRecommendationRules.GetActiveAsync(
            context.BusinessId,
            connection?.IntegrationConnectionId,
            DateTime.UtcNow,
            cancellationToken);
        if (rules.Count == 0)
            return null;

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIdentities(excluded, searchResults);
        AddIdentities(excluded, previouslyRecommended);
        await AddCartIdentitiesAsync(context, excluded, cancellationToken);

        var candidates = rules
            .Select(rule => new RuleCandidate(rule, MatchSpecificity(rule, searchResults)))
            .Where(candidate => candidate.Specificity > 0)
            .OrderByDescending(candidate => candidate.Specificity)
            .ThenByDescending(candidate => candidate.Rule.Priority)
            .ThenBy(candidate => candidate.Rule.ProductRecommendationRuleId)
            .ToList();

        var attemptedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var targetKey = ConfiguredTargetIdentity(candidate.Rule);
            if (!attemptedTargets.Add(targetKey) || excluded.Contains(targetKey))
                continue;

            try
            {
                var product = await _productLookup.GetProductAsync(
                    context,
                    new ProductLookupRequest(
                        candidate.Rule.RecommendedProductId,
                        FirstNonBlank(candidate.Rule.RecommendedExternalProductId, candidate.Rule.RecommendedProduct?.ExternalProductId),
                        FirstNonBlank(candidate.Rule.RecommendedSku, candidate.Rule.RecommendedProduct?.Sku),
                        candidate.Rule.RecommendedProduct?.Name,
                        candidate.Rule.RecommendedSearchText),
                    cancellationToken);
                if (product is null || ProductIdentities(product).Any(excluded.Contains))
                    continue;

                return new CatalogProductRecommendation(
                    product,
                    candidate.Rule.RecommendationType,
                    candidate.Rule.Reason);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Catalog recommendation target {TargetKey} could not be resolved for business {BusinessId}.",
                    targetKey,
                    context.BusinessId);
            }
        }

        return null;
    }

    private async Task AddCartIdentitiesAsync(
        AgentConversationContext context,
        ISet<string> excluded,
        CancellationToken cancellationToken)
    {
        var drafts = await _unitOfWork.OrderDrafts.GetActiveDraftsByConversationAsync(
            context.BusinessId,
            context.ConversationId,
            cancellationToken);
        var draft = drafts.FirstOrDefault();
        if (draft is null)
            return;

        var items = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(
            context.BusinessId,
            draft.OrderDraftId,
            cancellationToken);
        foreach (var item in items)
        {
            if (item.ProductId.HasValue)
                excluded.Add($"id:{item.ProductId.Value:N}");
            if (!string.IsNullOrWhiteSpace(item.ExternalProductId))
                excluded.Add($"external:{Normalize(item.ExternalProductId)}");
            if (!string.IsNullOrWhiteSpace(item.Sku))
                excluded.Add($"sku:{Normalize(item.Sku)}");
            if (!string.IsNullOrWhiteSpace(item.ProductNameSnapshot))
                excluded.Add($"name:{Normalize(item.ProductNameSnapshot)}");
        }
    }

    private static int MatchSpecificity(
        ProductRecommendationRule rule,
        IReadOnlyList<ProductReference> products) =>
        products.Any(product => Matches(rule, product))
            ? rule.MatchType switch
            {
                ProductRecommendationMatchType.Product => 500,
                ProductRecommendationMatchType.ProductClass => 400,
                ProductRecommendationMatchType.Subcategory => 300,
                ProductRecommendationMatchType.Family => 200,
                ProductRecommendationMatchType.Category => 100,
                _ => 0
            }
            : 0;

    private static bool Matches(ProductRecommendationRule rule, ProductReference product)
    {
        if (rule.MatchType == ProductRecommendationMatchType.Product)
        {
            if (rule.SourceProductId.HasValue && product.ProductId == rule.SourceProductId)
                return true;

            var sourceIdentity = FirstNonBlank(
                rule.SourceValue,
                rule.SourceProduct?.ExternalProductId,
                rule.SourceProduct?.Sku);
            return Same(sourceIdentity, product.ExternalProductId)
                   || Same(sourceIdentity, product.Sku);
        }

        var source = rule.MatchType switch
        {
            ProductRecommendationMatchType.Category => product.CategoryName,
            ProductRecommendationMatchType.Family => product.FamilyName,
            ProductRecommendationMatchType.Subcategory => product.SubcategoryName,
            ProductRecommendationMatchType.ProductClass => product.ProductClassName,
            _ => null
        };
        return Same(rule.SourceValue, source);
    }

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && Normalize(left) == Normalize(right);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string Normalize(string value) => CatalogSearchText.NormalizeCompact(value);

    private static string ConfiguredTargetIdentity(ProductRecommendationRule rule)
    {
        if (rule.RecommendedProductId.HasValue)
            return $"id:{rule.RecommendedProductId.Value:N}";
        if (!string.IsNullOrWhiteSpace(rule.RecommendedExternalProductId))
            return $"external:{Normalize(rule.RecommendedExternalProductId)}";
        return $"sku:{Normalize(rule.RecommendedSku ?? string.Empty)}";
    }

    private static IEnumerable<string> ProductIdentities(ProductReference product)
    {
        if (product.ProductId.HasValue)
            yield return $"id:{product.ProductId.Value:N}";
        if (!string.IsNullOrWhiteSpace(product.ExternalProductId))
            yield return $"external:{Normalize(product.ExternalProductId)}";
        if (!string.IsNullOrWhiteSpace(product.Sku))
            yield return $"sku:{Normalize(product.Sku)}";
        if (!string.IsNullOrWhiteSpace(product.Name))
            yield return $"name:{Normalize(product.Name)}";
    }

    private static void AddIdentities(ISet<string> identities, IEnumerable<ProductReference> products)
    {
        foreach (var product in products)
        foreach (var identity in ProductIdentities(product))
            identities.Add(identity);
    }

    private sealed record RuleCandidate(ProductRecommendationRule Rule, int Specificity);
}
