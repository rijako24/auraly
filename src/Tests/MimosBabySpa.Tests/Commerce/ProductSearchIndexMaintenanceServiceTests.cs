using FluentAssertions;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductSearchIndexMaintenanceServiceTests
{
    [Fact]
    public async Task Rebuild_ReindexesStableIdentityAndNormalizesSafeAliases()
    {
        var businessId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = businessId,
            Name = "ALKAPARRAS VINAGRE x500gr"
        };
        var alias = new ProductAlias
        {
            ProductAliasId = Guid.NewGuid(),
            BusinessId = businessId,
            ProductId = product.ProductId,
            Alias = "Alcaparras en vinagre",
            NormalizedAlias = string.Empty,
            Scope = ProductAliasScope.Business,
            Status = ProductAliasStatus.Active
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetIdentityCatalogAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([product]);
        products.Setup(repository => repository.UpdateAsync(product, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        products.Setup(repository => repository.ReplaceSearchTermsAsync(product, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var aliases = new Mock<IProductAliasRepository>();
        aliases.Setup(repository => repository.GetByBusinessAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([alias]);
        aliases.Setup(repository => repository.UpdateAsync(alias, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alias);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(unit => unit.Products).Returns(products.Object);
        unitOfWork.SetupGet(unit => unit.ProductAliases).Returns(aliases.Object);
        unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new ProductSearchIndexMaintenanceService(unitOfWork.Object);

        var result = await service.RebuildAsync(businessId);

        result.ProductsReindexed.Should().Be(1);
        result.AliasesNormalized.Should().Be(1);
        product.SearchIndexVersion.Should().Be(ProductSearchText.CurrentIndexVersion);
        alias.NormalizedAlias.Should().Be("alcaparra vinagre");
        products.Verify(repository => repository.ReplaceSearchTermsAsync(product, It.IsAny<CancellationToken>()), Times.Once);
        aliases.Verify(repository => repository.UpdateAsync(alias, It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
