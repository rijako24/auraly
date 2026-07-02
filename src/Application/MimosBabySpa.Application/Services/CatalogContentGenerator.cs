using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using DomainService = MimosBabySpa.Domain.Entities.Service;
using DomainServiceCategory = MimosBabySpa.Domain.Entities.ServiceCategory;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementacion de ICatalogContentGenerator.
/// </summary>
public class CatalogContentGenerator : ICatalogContentGenerator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CatalogContentGenerator> _logger;
    private readonly IPromotionPricingService _promotions;

    public CatalogContentGenerator(
        IUnitOfWork unitOfWork,
        ILogger<CatalogContentGenerator> logger,
        IPromotionPricingService promotions)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _promotions = promotions;
    }

    public Task<string> GenerateAsync(Guid businessId, CancellationToken ct = default) =>
        GenerateAsync(businessId, null, CatalogContentView.Services, ct);

    public Task<string> GenerateAsync(Guid businessId, string? query, CancellationToken ct = default) =>
        GenerateAsync(businessId, query, CatalogContentView.Services, ct);

    public async Task<string> GenerateAsync(
        Guid businessId,
        string? query,
        CatalogContentView view,
        CancellationToken ct = default)
    {
        try
        {
            var services = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
            var categories = await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId);
            var addOnRules = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
            var activeServices = services.Where(s => s.IsActive).ToList();
            var catalogServices = FilterByQuery(activeServices, categories, query);

            var promotionPricing = await _promotions.EvaluateAsync(
                businessId,
                catalogServices.Select(s => new PromotionPricingItem(
                    s.ServiceId.ToString("N"),
                    PromotionItemType.Service,
                    null,
                    s.ServiceId,
                    s.ServiceName,
                    s.ServiceCategory?.Name,
                    s.Price,
                    1,
                    s.IncludeInCheckoutTotal)).ToList(),
                ct: ct);

            var priceByService = promotionPricing.Items.ToDictionary(i => i.Item.Key, StringComparer.OrdinalIgnoreCase);

            var serviceInfos = catalogServices
                .Select(s =>
                {
                    priceByService.TryGetValue(s.ServiceId.ToString("N"), out var priced);
                    var hasPromotion = priced?.HasPromotion == true;
                    return new ServiceInfo
                    {
                        ServiceId = s.ServiceId,
                        Name = s.ServiceName,
                        Description = s.Description,
                        DurationMinutes = s.DurationMinutes,
                        Price = s.Price,
                        EffectivePrice = hasPromotion ? priced!.EffectiveUnitPrice : null,
                        DiscountAmount = hasPromotion ? priced!.DiscountAmount : null,
                        PromotionName = hasPromotion ? priced!.PromotionName : null,
                        PromotionSummary = hasPromotion ? priced!.PromotionSummary : null,
                        IsActive = s.IsActive,
                        CategoryId = s.CategoryId,
                        CategoryName = s.ServiceCategory?.Name ?? string.Empty,
                        CategoryDisplayOrder = s.ServiceCategory?.DisplayOrder ?? 0,
                        Tier = s.Tier,
                        ServiceType = s.ServiceType,
                        FulfillmentKind = s.FulfillmentKind,
                        FixedScheduleLabel = s.FixedScheduleLabel,
                        BundleItems = s.BundleItems
                            .OrderBy(b => b.DisplayOrder)
                            .Select(b => new BundleItemInfo
                            {
                                Name = b.IncludedService.ServiceName,
                                Description = b.IncludedService.Description,
                                Price = b.IncludedService.Price,
                                DisplayOrder = b.DisplayOrder
                            })
                            .ToList()
                    };
                })
                .ToList();

            var categoryInfos = categories
                .Select(sc => new CategoryInfo
                {
                    CategoryId = sc.ServiceCategoryId,
                    Name = sc.Name,
                    Description = sc.Description,
                    DisplayOrder = sc.DisplayOrder
                })
                .ToList();

            var addOnRuleInfos = addOnRules
                .Select(r => new AddOnRuleInfo
                {
                    AddOnName = r.AddOnService.ServiceName,
                    AddOnDescription = r.AddOnService.Description,
                    AddOnPrice = r.AddOnService.Price,
                    IncludeInCheckoutTotal = r.AddOnService.IncludeInCheckoutTotal,
                    DisplayOrder = r.DisplayOrder,
                    CompatibleWithServiceName = r.CompatibleService?.ServiceName,
                    CompatibleCategoryId = r.CompatibleService?.CategoryId,
                    CompatibleCategoryName = r.CompatibleService?.ServiceCategory?.Name
                })
                .OrderBy(r => r.DisplayOrder)
                .ThenBy(r => r.AddOnName)
                .ToList();

            return view == CatalogContentView.Categories
                ? ServiceCatalogBuilder.BuildCategoryOverview(serviceInfos, categoryInfos)
                : ServiceCatalogBuilder.Build(serviceInfos, addOnRuleInfos, categoryInfos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando catalogo dinamico para BusinessId={BusinessId}", businessId);
            return string.Empty;
        }
    }

    private static List<DomainService> FilterByQuery(
        IReadOnlyList<DomainService> services,
        IEnumerable<DomainServiceCategory> categories,
        string? query)
    {
        var terms = CatalogSearchText.GetSearchTerms(query);
        if (terms.Count == 0)
            return services.ToList();

        var categoryList = categories.ToList();
        var selectedCategories = categoryList
            .Where(c => MatchesCategorySelection(c, query, terms))
            .ToList();
        if (selectedCategories.Count > 0)
        {
            var selectedCategoryIds = selectedCategories
                .Select(c => c.ServiceCategoryId)
                .ToHashSet();

            var categoryServices = services
                .Where(s => selectedCategoryIds.Contains(s.CategoryId))
                .ToList();
            if (categoryServices.Count > 0)
                return categoryServices;
        }

        var allTermMatches = services
            .Where(s => MatchesServiceText(s, terms, requireAllTerms: true))
            .ToList();
        if (allTermMatches.Count > 0)
            return allTermMatches;

        var directMatches = services
            .Where(s => MatchesServiceText(s, terms, requireAllTerms: false))
            .ToList();
        if (directMatches.Count > 0)
            return directMatches;

        var categoryById = categoryList.ToDictionary(c => c.ServiceCategoryId);
        return services
            .Where(s => MatchesCategoryText(categoryById.TryGetValue(s.CategoryId, out var cat) ? cat : null, terms))
            .ToList();
    }

    private static bool MatchesServiceText(
        DomainService service,
        IReadOnlyList<string> terms,
        bool requireAllTerms)
    {
        var haystack = NormalizeSearchText(string.Join(' ', new[]
        {
            service.ServiceName,
            service.Description,
            service.Keywords
        }.Where(s => !string.IsNullOrWhiteSpace(s))));

        return requireAllTerms
            ? terms.All(term => ContainsSearchTerm(haystack, term))
            : terms.Any(term => ContainsSearchTerm(haystack, term));
    }

    private static bool MatchesCategorySelection(
        DomainServiceCategory category,
        string? query,
        IReadOnlyList<string> terms)
    {
        var categoryName = CatalogSearchText.NormalizeCompact(category.Name);
        if (string.IsNullOrWhiteSpace(categoryName))
            return false;

        var compactQuery = CatalogSearchText.NormalizeCompact(query);
        return TermsEquivalent(categoryName, compactQuery)
               || (terms.Count == 1
                   && terms.Any(term => TermsEquivalent(categoryName, CatalogSearchText.NormalizeCompact(term))));
    }

    private static bool MatchesCategoryText(DomainServiceCategory? category, IReadOnlyList<string> terms)
    {
        if (category is null)
            return false;

        var haystack = NormalizeSearchText(string.Join(' ', new[]
        {
            category.Name,
            category.Description
        }.Where(s => !string.IsNullOrWhiteSpace(s))));

        return terms.Any(term => ContainsSearchTerm(haystack, term));
    }

    private static bool ContainsSearchTerm(string haystack, string term)
    {
        var normalizedTerm = NormalizeSearchText(term);
        return !string.IsNullOrWhiteSpace(normalizedTerm)
               && (haystack.Contains(normalizedTerm, StringComparison.Ordinal)
                   || (normalizedTerm.EndsWith('s')
                       && normalizedTerm.Length > 3
                       && haystack.Contains(normalizedTerm[..^1], StringComparison.Ordinal)));
    }

    private static bool TermsEquivalent(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && (left.Equals(right, StringComparison.Ordinal)
                   || (left.EndsWith('s') && left.Length > 3 && left[..^1].Equals(right, StringComparison.Ordinal))
                   || (right.EndsWith('s') && right.Length > 3 && right[..^1].Equals(left, StringComparison.Ordinal)));
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
