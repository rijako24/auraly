using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Catalog;
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
    private const int CatalogSearchLimit = 50;

    private readonly IServiceCatalogPricingService _catalogPricing;

    public CatalogContentGenerator(
        IUnitOfWork unitOfWork,
        ILogger<CatalogContentGenerator> logger,
        IServiceCatalogPricingService catalogPricing)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _catalogPricing = catalogPricing;
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
            var categories = (await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId)).ToList();
            var categoryInfos = BuildCategoryInfos(categories);
            var searchTerms = CatalogSearchText.GetSearchTerms(query);
            var useCatalogSearch = view != CatalogContentView.Categories && searchTerms.Count > 0;
            var services = await LoadCatalogServicesAsync(businessId, searchTerms, useCatalogSearch, ct);

            if (view == CatalogContentView.Services && useCatalogSearch && services.Count == 0)
                services = await LoadActiveServicesAsync(businessId, ct);

            var catalogServices = ResolveCatalogServices(services, categories, query, view, out var effectiveView);

            if (effectiveView == CatalogContentView.Categories)
            {
                if (useCatalogSearch)
                    services = await LoadActiveServicesAsync(businessId, ct);

                var overviewServices = await _catalogPricing.BuildServiceInfosAsync(
                    businessId,
                    services,
                    applyPromotions: false,
                    ct);
                return ServiceCatalogBuilder.BuildCategoryOverview(overviewServices, categoryInfos);
            }

            var serviceInfos = await _catalogPricing.BuildServiceInfosAsync(
                businessId,
                catalogServices,
                applyPromotions: true,
                ct);
            var addOnRules = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
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

            return ServiceCatalogBuilder.Build(serviceInfos, addOnRuleInfos, categoryInfos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando catalogo dinamico para BusinessId={BusinessId}", businessId);
            return string.Empty;
        }
    }

    private async Task<List<DomainService>> LoadCatalogServicesAsync(
        Guid businessId,
        IReadOnlyList<string> searchTerms,
        bool useCatalogSearch,
        CancellationToken ct)
    {
        if (!useCatalogSearch)
            return await LoadActiveServicesAsync(businessId, ct);

        return (await _unitOfWork.Services.SearchActiveCatalogAsync(
                businessId,
                searchTerms,
                CatalogSearchLimit,
                ct))
            .ToList();
    }

    private async Task<List<DomainService>> LoadActiveServicesAsync(Guid businessId, CancellationToken ct) =>
        (await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId)).ToList();

    private static IReadOnlyList<DomainService> ResolveCatalogServices(
        IReadOnlyList<DomainService> services,
        IReadOnlyList<DomainServiceCategory> categories,
        string? query,
        CatalogContentView requestedView,
        out CatalogContentView effectiveView)
    {
        if (requestedView == CatalogContentView.Categories)
        {
            effectiveView = CatalogContentView.Categories;
            return services;
        }

        if (requestedView == CatalogContentView.Services)
        {
            effectiveView = CatalogContentView.Services;
            return FilterByQuery(services, categories, query);
        }

        var matches = FindCatalogMatches(services, categories, query);
        if (matches.Count == 0)
        {
            effectiveView = CatalogContentView.Categories;
            return services;
        }

        effectiveView = CatalogContentView.Services;
        return matches;
    }

    private static IReadOnlyList<CategoryInfo> BuildCategoryInfos(IEnumerable<DomainServiceCategory> categories) =>
        categories
            .Select(sc => new CategoryInfo
            {
                CategoryId = sc.ServiceCategoryId,
                Name = sc.Name,
                Description = sc.Description,
                DisplayOrder = sc.DisplayOrder
            })
            .ToList();

    private static List<DomainService> FilterByQuery(
        IReadOnlyList<DomainService> services,
        IEnumerable<DomainServiceCategory> categories,
        string? query)
    {
        var rawTerms = CatalogSearchText.GetSearchTerms(query);
        if (rawTerms.Count == 0)
            return services.ToList();

        var matches = FindCatalogMatches(services, categories, query, rawTerms);
        return matches.Count == 0 ? services.ToList() : matches;
    }

    private static List<DomainService> FindCatalogMatches(
        IReadOnlyList<DomainService> services,
        IEnumerable<DomainServiceCategory> categories,
        string? query)
    {
        var rawTerms = CatalogSearchText.GetSearchTerms(query);
        return rawTerms.Count == 0
            ? []
            : FindCatalogMatches(services, categories, query, rawTerms);
    }

    private static List<DomainService> FindCatalogMatches(
        IReadOnlyList<DomainService> services,
        IEnumerable<DomainServiceCategory> categories,
        string? query,
        IReadOnlyList<string> rawTerms)
    {
        var categoryList = categories.ToList();
        var terms = ExtractStructuredCatalogTerms(services, categoryList, rawTerms);
        if (terms.Count == 0)
            return [];

        var selectedCategories = categoryList
            .Where(c => MatchesCategorySelection(c, query, terms))
            .ToList();
        if (selectedCategories.Count > 0)
        {
            var selectedCategoryIds = selectedCategories
                .Select(c => c.ServiceCategoryId)
                .ToHashSet();

            var categoryServices = services
                .Where(s => s.CategoryId.HasValue && selectedCategoryIds.Contains(s.CategoryId.Value))
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
            .Where(s => s.CategoryId.HasValue && MatchesCategoryText(categoryById.TryGetValue(s.CategoryId.Value, out var cat) ? cat : null, terms))
            .ToList();
    }

    private static IReadOnlyList<string> ExtractStructuredCatalogTerms(
        IReadOnlyList<DomainService> services,
        IReadOnlyList<DomainServiceCategory> categories,
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return [];

        var identityTexts = services
            .Select(s => NormalizeSearchText(string.Join(' ', new[]
            {
                s.ServiceName,
                s.Keywords
            }.Where(value => !string.IsNullOrWhiteSpace(value)))))
            .Concat(categories.Select(c => NormalizeSearchText(c.Name)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return terms
            .Where(term => identityTexts.Any(text => ContainsSearchTerm(text, term)))
            .Distinct(StringComparer.Ordinal)
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
        if (TermsEquivalent(categoryName, compactQuery))
            return true;

        var categoryTokens = CatalogSearchText.GetSearchTerms(category.Name);
        return terms.Count > 0
               && terms.All(term => categoryTokens.Any(token => TermsMatch(token, term)));
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
        if (string.IsNullOrWhiteSpace(normalizedTerm))
            return false;

        return haystack
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => TermsMatch(token, normalizedTerm));
    }

    private static bool TermsMatch(string catalogTerm, string queryTerm) =>
        TermsEquivalent(catalogTerm, queryTerm)
        || (queryTerm.Length >= 4 && catalogTerm.StartsWith(queryTerm, StringComparison.Ordinal))
        || (catalogTerm.Length >= 4 && queryTerm.StartsWith(catalogTerm, StringComparison.Ordinal));

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
