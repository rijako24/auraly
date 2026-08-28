using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ProductAdminServiceTests
{
    [Fact]
    public async Task Update_WhenNothingChanged_DoesNotPersistAuditOrRebuildSearchIndex()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAsync(
            fixture.TenantId,
            fixture.BusinessId,
            fixture.Product.ProductId,
            new UpdateProductRequest("Producto", "REF-1", "Descripcion", "Categoria"));

        result.Name.Should().Be("Producto");
        fixture.Products.Verify(repository => repository.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_DoesNotExposeAParallelPriceWriteContract()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAsync(
            fixture.TenantId,
            fixture.BusinessId,
            fixture.Product.ProductId,
            new UpdateProductRequest("Producto", "REF-1", "Descripcion", "Categoria"));

        result.UnitPrice.Should().Be(100m);
        fixture.Products.Verify(repository => repository.UpdateAsync(fixture.Product, It.IsAny<CancellationToken>()), Times.Never);
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WhenSearchableIdentityChanged_RebuildsSearchIndexOnce()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAsync(
            fixture.TenantId,
            fixture.BusinessId,
            fixture.Product.ProductId,
            new UpdateProductRequest("Producto actualizado", "REF-2", "Descripcion", "Categoria"));

        result.Name.Should().Be("Producto actualizado");
        result.Reference.Should().Be("REF-2");
        fixture.Product.Sku.Should().Be("REF-2");
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(fixture.Product, It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var category = new ProductCategory
        {
            ProductCategoryId = Guid.NewGuid(),
            BusinessId = businessId,
            Name = "Categoria",
            IsActive = true,
            IsBrowsable = true,
            CreatedAt = DateTime.UtcNow
        };
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = businessId,
            Name = "Producto",
            Reference = "REF-1",
            Sku = "REF-1",
            Description = "Descripcion",
            CategoryName = "Categoria",
            UnitPrice = 100m,
            ProductCategoryId = category.ProductCategoryId,
            Currency = "COP",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var businesses = new Mock<IBusinessRepository>();
        businesses.Setup(repository => repository.GetByIdAsync(businessId))
            .ReturnsAsync(new Business { BusinessId = businessId, TenantId = tenantId, Name = "Negocio" });
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByIdAsync(businessId, product.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        products.Setup(repository => repository.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product value, CancellationToken _) => value);
        products.Setup(repository => repository.ReplaceSearchTermsAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(unit => unit.Businesses).Returns(businesses.Object);
        var categories = new Mock<IProductCategoryRepository>();
        categories.Setup(repository => repository.GetByNameAsync(
                businessId, null, "Categoria", It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        unitOfWork.SetupGet(unit => unit.Products).Returns(products.Object);
        unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.SetupGet(unit => unit.ProductCategories).Returns(categories.Object);
        return new Fixture(
            tenantId,
            businessId,
            product,
            products,
            unitOfWork,
            new ProductAdminService(unitOfWork.Object));
    }

    private sealed record Fixture(
        Guid TenantId,
        Guid BusinessId,
        Product Product,
        Mock<IProductRepository> Products,
        Mock<IUnitOfWork> UnitOfWork,
        ProductAdminService Service);
}
