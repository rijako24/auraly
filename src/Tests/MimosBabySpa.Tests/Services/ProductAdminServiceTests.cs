using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Identity.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

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
            new UpdateProductRequest("Producto", "Descripcion", "Categoria", 100m, "cop"));

        result.Name.Should().Be("Producto");
        fixture.Products.Verify(repository => repository.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Audit.Verify(audit => audit.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WhenOnlyPriceChanged_PersistsWithoutRebuildingSearchIndex()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAsync(
            fixture.TenantId,
            fixture.BusinessId,
            fixture.Product.ProductId,
            new UpdateProductRequest("Producto", "Descripcion", "Categoria", 125m, "COP"));

        result.UnitPrice.Should().Be(125m);
        fixture.Products.Verify(repository => repository.UpdateAsync(fixture.Product, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WhenSearchableIdentityChanged_RebuildsSearchIndexOnce()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAsync(
            fixture.TenantId,
            fixture.BusinessId,
            fixture.Product.ProductId,
            new UpdateProductRequest("Producto actualizado", "Descripcion", "Categoria", 100m, "COP"));

        result.Name.Should().Be("Producto actualizado");
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
        var audit = new Mock<IAuditService>();
        unitOfWork.SetupGet(unit => unit.ProductCategories).Returns(categories.Object);
        audit.Setup(service => service.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new Fixture(
            tenantId,
            businessId,
            product,
            products,
            unitOfWork,
            audit,
            new ProductAdminService(unitOfWork.Object, audit.Object));
    }

    private sealed record Fixture(
        Guid TenantId,
        Guid BusinessId,
        Product Product,
        Mock<IProductRepository> Products,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IAuditService> Audit,
        ProductAdminService Service);
}