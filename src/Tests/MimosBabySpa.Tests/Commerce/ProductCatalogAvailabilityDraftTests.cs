using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductCatalogAvailabilityDraftTests
{
    [Fact]
    public async Task FindUnavailableDraftItemsAsync_ReturnsInactiveOutOfStockAndMissingItems()
    {
        var businessId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var outOfStockId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var products = new Mock<IProductRepository>();
        products.Setup(p => p.GetByIdAsync(businessId, activeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product(activeId, isActive: true, manageStock: false, stock: null));
        products.Setup(p => p.GetByIdAsync(businessId, inactiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product(inactiveId, isActive: false, manageStock: false, stock: null));
        products.Setup(p => p.GetByIdAsync(businessId, outOfStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product(outOfStockId, isActive: true, manageStock: true, stock: 0));
        products.Setup(p => p.GetByIdAsync(businessId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Products).Returns(products.Object);
        var service = new ProductCatalogAvailabilityService(unitOfWork.Object);

        var unavailable = await service.FindUnavailableDraftItemsAsync(
            businessId,
            [
                Item(activeId, "Mango"),
                Item(inactiveId, "Dulce"),
                Item(outOfStockId, "Semidulce"),
                Item(missingId, "Premium")
            ],
            CancellationToken.None);

        unavailable.Select(i => i.ProductName).Should().BeEquivalentTo("Dulce", "Semidulce", "Premium");
        unavailable.Should().Contain(i => i.ProductId == inactiveId && i.Reason == "inactive");
        unavailable.Should().Contain(i => i.ProductId == outOfStockId && i.Reason == "unavailable");
        unavailable.Should().Contain(i => i.ProductId == missingId && i.Reason == "not_found");
    }

    [Fact]
    public async Task FindUnavailableDraftItemsAsync_LoadsEachProductOnlyOnce()
    {
        var businessId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var products = new Mock<IProductRepository>();
        products.Setup(p => p.GetByIdAsync(businessId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product(productId, isActive: false, manageStock: false, stock: null));
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Products).Returns(products.Object);
        var service = new ProductCatalogAvailabilityService(unitOfWork.Object);

        var unavailable = await service.FindUnavailableDraftItemsAsync(
            businessId,
            [Item(productId, "Dulce 750ML"), Item(productId, "Dulce 750ML")],
            CancellationToken.None);

        unavailable.Should().HaveCount(2);
        products.Verify(p => p.GetByIdAsync(businessId, productId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Product Product(Guid productId, bool isActive, bool manageStock, decimal? stock) => new()
    {
        ProductId = productId,
        IsActive = isActive,
        ManageStock = manageStock,
        StockQuantity = stock
    };

    private static OrderDraftItem Item(Guid productId, string name) => new()
    {
        OrderDraftItemId = Guid.NewGuid(),
        ProductId = productId,
        ProductNameSnapshot = name,
        Sku = name.ToUpperInvariant()
    };
}
