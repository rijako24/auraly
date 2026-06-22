using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

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

    public async Task<string> GenerateAsync(Guid businessId, CancellationToken ct = default)
    {
        try
        {
            var services      = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
            var categories    = await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId);
            var addOnRules    = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
            var activeServices = services.Where(s => s.IsActive).ToList();

            var promotionPricing = await _promotions.EvaluateAsync(
                businessId,
                activeServices.Select(s => new PromotionPricingItem(
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

            var serviceInfos = activeServices
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
}
