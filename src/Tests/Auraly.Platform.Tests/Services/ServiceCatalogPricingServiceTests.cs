using FluentAssertions;
using Auraly.Platform.Application.Promotions;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public class ServiceCatalogPricingServiceTests
{
    [Fact]
    public async Task BuildServiceInfosAsync_WhenPromotionsEnabled_AppliesEffectivePrice()
    {
        var businessId = Guid.NewGuid();
        var service = BuildService(businessId, "Corte premium", 100m);
        var promotions = new Mock<IPromotionPricingService>();
        promotions
            .Setup(p => p.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<PromotionPricingItem> items, DateTime? _, CancellationToken _) =>
            {
                var adjustment = new PromotionAppliedAdjustment(Guid.NewGuid(), "Promo julio", 20m, "Promo julio: descuento");
                var priced = new PromotionPricedItem(items[0], 100m, 20m, 80m, 80m, [adjustment]);
                return new PromotionPricingResult([priced], 100m, 20m, 80m);
            });

        var sut = new ServiceCatalogPricingService(promotions.Object);

        var result = await sut.BuildServiceInfosAsync(businessId, [service], applyPromotions: true);

        result.Should().ContainSingle();
        result[0].Price.Should().Be(100m);
        result[0].EffectivePrice.Should().Be(80m);
        result[0].DiscountAmount.Should().Be(20m);
        result[0].PromotionName.Should().Be("Promo julio");
        result[0].PromotionSummary.Should().Be("Promo julio: descuento");
    }

    [Fact]
    public async Task BuildServiceInfosAsync_WhenPromotionsDisabled_DoesNotEvaluatePromotions()
    {
        var businessId = Guid.NewGuid();
        var service = BuildService(businessId, "Corte base", 50m);
        var promotions = new Mock<IPromotionPricingService>(MockBehavior.Strict);
        var sut = new ServiceCatalogPricingService(promotions.Object);

        var result = await sut.BuildServiceInfosAsync(businessId, [service], applyPromotions: false);

        result.Should().ContainSingle();
        result[0].Price.Should().Be(50m);
        result[0].EffectivePrice.Should().BeNull();
        result[0].PromotionSummary.Should().BeNull();
        promotions.VerifyNoOtherCalls();
    }

    private static Service BuildService(Guid businessId, string name, decimal price)
    {
        var categoryId = Guid.NewGuid();
        return new Service
        {
            BusinessId = businessId,
            ServiceId = Guid.NewGuid(),
            ServiceName = name,
            Description = "Servicio de prueba",
            DurationMinutes = 45,
            Price = price,
            IsActive = true,
            CategoryId = categoryId,
            ServiceCategory = new ServiceCategory
            {
                BusinessId = businessId,
                ServiceCategoryId = categoryId,
                Name = "Corte",
                DisplayOrder = 1
            },
            ServiceType = ServiceType.Standard
        };
    }
}
