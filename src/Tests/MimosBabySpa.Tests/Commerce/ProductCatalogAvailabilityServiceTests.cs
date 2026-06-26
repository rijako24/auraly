using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductCatalogAvailabilityServiceTests
{
    [Fact]
    public void FilterSellable_RemovesInactiveAndUnavailableProducts()
    {
        var service = new ProductCatalogAvailabilityService(new Mock<IUnitOfWork>().Object);
        var sellable = Product("Mango 750ML", isActive: true, isAvailable: true);
        var inactive = Product("Dulce 750ML", isActive: false, isAvailable: true);
        var unavailable = Product("Semidulce 750ML", isActive: true, isAvailable: false);

        var result = service.FilterSellable(new ProductSearchResult(
            [sellable, inactive, unavailable],
            "local"));

        result.Products.Should().ContainSingle();
        result.Products[0].Name.Should().Be("Mango 750ML");
    }

    [Fact]
    public void IsSellable_ForLocalProduct_RequiresActiveAndAvailableStockWhenManaged()
    {
        var service = new ProductCatalogAvailabilityService(new Mock<IUnitOfWork>().Object);

        service.IsSellable(new Product { IsActive = true, ManageStock = false }).Should().BeTrue();
        service.IsSellable(new Product { IsActive = true, ManageStock = true, StockQuantity = 1 }).Should().BeTrue();
        service.IsSellable(new Product { IsActive = true, ManageStock = true, StockQuantity = 0 }).Should().BeFalse();
        service.IsSellable(new Product { IsActive = false, ManageStock = false }).Should().BeFalse();
    }

    private static ProductReference Product(string name, bool isActive, bool isAvailable) =>
        new(
            Guid.NewGuid(),
            null,
            null,
            name,
            null,
            null,
            1000m,
            "COP",
            null,
            isAvailable)
        { IsActive = isActive };
}
