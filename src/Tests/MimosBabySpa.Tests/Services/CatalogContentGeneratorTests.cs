using FluentAssertions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class CatalogContentGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_AutoViewWithQuery_UsesRepositorySearchAndPricingService()
    {
        var businessId = Guid.NewGuid();
        var category = new ServiceCategory
        {
            ServiceCategoryId = Guid.NewGuid(),
            BusinessId = businessId,
            Name = "Cortes",
            DisplayOrder = 1,
            IsActive = true
        };
        var service = new Service
        {
            ServiceId = Guid.NewGuid(),
            BusinessId = businessId,
            ServiceName = "Corte de cabello",
            Description = "Corte clasico",
            DurationMinutes = 30,
            Price = 50000m,
            IsActive = true,
            CategoryId = category.ServiceCategoryId,
            ServiceCategory = category,
            ServiceType = ServiceType.Standard,
            Tier = ServiceTier.Base
        };

        var services = new Mock<IServiceRepository>(MockBehavior.Strict);
        services
            .Setup(r => r.SearchActiveCatalogAsync(
                businessId,
                It.Is<IReadOnlyList<string>>(terms => terms.Contains("corte") && terms.Contains("cabello")),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([service]);

        var categories = new Mock<IServiceCategoryRepository>(MockBehavior.Strict);
        categories
            .Setup(r => r.GetByBusinessIdAsync(businessId))
            .ReturnsAsync([category]);

        var addOnRules = new Mock<IServiceAddOnRuleRepository>(MockBehavior.Strict);
        addOnRules
            .Setup(r => r.GetByBusinessIdAsync(businessId))
            .ReturnsAsync([]);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(services.Object);
        unitOfWork.SetupGet(u => u.ServiceCategories).Returns(categories.Object);
        unitOfWork.SetupGet(u => u.ServiceAddOnRules).Returns(addOnRules.Object);

        var pricing = new Mock<IServiceCatalogPricingService>(MockBehavior.Strict);
        pricing
            .Setup(p => p.BuildServiceInfosAsync(
                businessId,
                It.Is<IReadOnlyList<Service>>(items => items.Count == 1 && items[0].ServiceId == service.ServiceId),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ServiceInfo
                {
                    ServiceId = service.ServiceId,
                    Name = service.ServiceName,
                    Description = service.Description,
                    DurationMinutes = service.DurationMinutes,
                    Price = service.Price,
                    EffectivePrice = 40000m,
                    DiscountAmount = 10000m,
                    PromotionName = "Promo",
                    PromotionSummary = "Promo activa",
                    IsActive = true,
                    CategoryId = category.ServiceCategoryId,
                    CategoryName = category.Name,
                    CategoryDisplayOrder = category.DisplayOrder,
                    Tier = service.Tier,
                    ServiceType = service.ServiceType,
                    FulfillmentKind = service.FulfillmentKind,
                    BundleItems = []
                }
            ]);

        var sut = new CatalogContentGenerator(
            unitOfWork.Object,
            Mock.Of<ILogger<CatalogContentGenerator>>(),
            pricing.Object);

        var catalog = await sut.GenerateAsync(businessId, "quiero corte de cabello", CatalogContentView.Auto);

        catalog.Should().Contain("Corte de cabello");
        catalog.Should().Contain("precio promocional");
        services.Verify(r => r.SearchActiveCatalogAsync(
            businessId,
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        services.Verify(r => r.GetActiveByBusinessIdAsync(It.IsAny<Guid>()), Times.Never);
        pricing.VerifyAll();
    }
}