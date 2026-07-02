using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Promotions;
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
        GenerateAsync(businessId, null, ct);

    public async Task<string> GenerateAsync(Guid businessId, string? query, CancellationToken ct = default)
    {
        try
        {
            var services      = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
            var categories    = await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId);
            var addOnRules    = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
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
                        ServiceId             = s.ServiceId,
                        Name                  = s.ServiceName,
                        Description           = s.Description,
                        DurationMinutes       = s.DurationMinutes,
                        Price                 = s.Price,
                        EffectivePrice        = hasPromotion ? priced!.EffectiveUnitPrice : null,
                        DiscountAmount        = hasPromotion ? priced!.DiscountAmount : null,
                        PromotionName         = hasPromotion ? priced!.PromotionName : null,
                        PromotionSummary      = hasPromotion ? priced!.PromotionSummary : null,
                        IsActive              = s.IsActive,
                        CategoryId            = s.CategoryId,
                        CategoryName          = s.ServiceCategory?.Name ?? string.Empty,
                        CategoryDisplayOrder  = s.ServiceCategory?.DisplayOrder ?? 0,
                        Tier                  = s.Tier,
                        ServiceType           = s.ServiceType,
                        FulfillmentKind       = s.FulfillmentKind,
                        FixedScheduleLabel    = s.FixedScheduleLabel,
                        BundleItems           = s.BundleItems
                            .OrderBy(b => b.DisplayOrder)
                            .Select(b => new BundleItemInfo
                            {
                                Name         = b.IncludedService.ServiceName,
                                Description  = b.IncludedService.Description,
                                Price        = b.IncludedService.Price,
                                DisplayOrder = b.DisplayOrder
                            })
                            .ToList()
                    };
                })
                .ToList();

            var categoryInfos = categories
                .Select(sc => new CategoryInfo
                {
                    CategoryId   = sc.ServiceCategoryId,
                    Name         = sc.Name,
                    Description  = sc.Description,
                    DisplayOrder = sc.DisplayOrder
                })
                .ToList();

            var addOnRuleInfos = addOnRules
                .Select(r => new AddOnRuleInfo
                {
                    AddOnName                 = r.AddOnService.ServiceName,
                    AddOnDescription          = r.AddOnService.Description,
                    AddOnPrice                = r.AddOnService.Price,
                    IncludeInCheckoutTotal    = r.AddOnService.IncludeInCheckoutTotal,
                    DisplayOrder              = r.DisplayOrder,
                    CompatibleWithServiceName = r.CompatibleService?.ServiceName,
                    CompatibleCategoryId      = r.CompatibleService?.CategoryId,
                    CompatibleCategoryName    = r.CompatibleService?.ServiceCategory?.Name
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


    private static List<DomainService> FilterByQuery(
        IReadOnlyList<DomainService> services,
        IEnumerable<DomainServiceCategory> categories,
        string? query)
    {
        var terms = GetSearchTerms(query);
        if (terms.Count == 0)
            return services.ToList();

        var directMatches = services
            .Where(s => MatchesServiceText(s, terms))
            .ToList();

        if (directMatches.Count > 0)
            return directMatches;

        var categoryById = categories.ToDictionary(c => c.ServiceCategoryId);
        return services
            .Where(s => MatchesCategoryText(categoryById.TryGetValue(s.CategoryId, out var cat) ? cat : null, terms))
            .ToList();
    }

    private static bool MatchesServiceText(DomainService service, IReadOnlyList<string> terms)
    {
        var haystack = NormalizeSearchText(string.Join(' ', new[]
        {
            service.ServiceName,
            service.Description
        }.Where(s => !string.IsNullOrWhiteSpace(s))));

        return terms.Any(haystack.Contains);
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

        return terms.Any(haystack.Contains);
    }

    private static IReadOnlyList<string> GetSearchTerms(string? query)
    {
        var normalized = NormalizeSearchText(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "quiero", "quiere", "servicio", "servicios", "precio", "precios", "opcion", "opciones",
            "para", "con", "sin", "del", "los", "las", "una", "uno", "unos", "unas", "que", "hay",
            "tienen", "tiene", "cabello"
        };

        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length <= 2 || stopWords.Contains(token))
                continue;

            terms.Add(token);
            if (token.EndsWith('s') && token.Length > 3)
                terms.Add(token[..^1]);
        }

        return terms.ToList();
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
