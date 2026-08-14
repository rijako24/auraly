using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Promotions;
using Auraly.Platform.Domain.Enums;
using DomainService = Auraly.Platform.Domain.Entities.Service;

namespace Auraly.Platform.Application.Services;

public interface IServiceCatalogPricingService
{
    Task<IReadOnlyList<ServiceInfo>> BuildServiceInfosAsync(
        Guid businessId,
        IReadOnlyList<DomainService> services,
        bool applyPromotions,
        CancellationToken ct = default);
}

public sealed class ServiceCatalogPricingService : IServiceCatalogPricingService
{
    private readonly IPromotionPricingService _promotions;

    public ServiceCatalogPricingService(IPromotionPricingService promotions) => _promotions = promotions;

    public async Task<IReadOnlyList<ServiceInfo>> BuildServiceInfosAsync(
        Guid businessId,
        IReadOnlyList<DomainService> services,
        bool applyPromotions,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, PromotionPricedItem>? priceByService = null;
        if (applyPromotions && services.Count > 0)
        {
            var promotionPricing = await _promotions.EvaluateAsync(
                businessId,
                services.Select(s => new PromotionPricingItem(
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

            priceByService = promotionPricing.Items.ToDictionary(
                item => item.Item.Key,
                StringComparer.OrdinalIgnoreCase);
        }

        return services.Select(service => BuildServiceInfo(service, priceByService)).ToList();
    }

    private static ServiceInfo BuildServiceInfo(
        DomainService service,
        IReadOnlyDictionary<string, PromotionPricedItem>? priceByService)
    {
        PromotionPricedItem? priced = null;
        priceByService?.TryGetValue(service.ServiceId.ToString("N"), out priced);
        var hasPromotion = priced?.HasPromotion == true;

        return new ServiceInfo
        {
            ServiceId = service.ServiceId,
            Name = service.ServiceName,
            Description = service.Description,
            DurationMinutes = service.DurationMinutes,
            Price = service.Price,
            EffectivePrice = hasPromotion ? priced!.EffectiveUnitPrice : null,
            DiscountAmount = hasPromotion ? priced!.DiscountAmount : null,
            PromotionName = hasPromotion ? priced!.PromotionName : null,
            PromotionSummary = hasPromotion ? priced!.PromotionSummary : null,
            IsActive = service.IsActive,
            CategoryId = service.CategoryId,
            CategoryName = service.ServiceCategory?.Name ?? string.Empty,
            CategoryDisplayOrder = service.ServiceCategory?.DisplayOrder ?? 0,
            Tier = service.Tier,
            ServiceType = service.ServiceType,
            FulfillmentKind = service.FulfillmentKind,
            FixedScheduleLabel = service.FixedScheduleLabel,
            BundleItems = service.BundleItems
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new BundleItemInfo
                {
                    Name = item.IncludedService.ServiceName,
                    Description = item.IncludedService.Description,
                    Price = item.IncludedService.Price,
                    DisplayOrder = item.DisplayOrder
                })
                .ToList()
        };
    }
}
