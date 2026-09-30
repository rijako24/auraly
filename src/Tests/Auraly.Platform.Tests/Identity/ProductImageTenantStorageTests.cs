using Azure.Storage;
using Azure.Storage.Blobs;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class ProductImageTenantStorageTests
{
    [Fact]
    public async Task Tenant_image_resolver_signs_the_tenant_container_without_a_blob_read()
    {
        var tenantId = Guid.NewGuid();
        var client = new BlobServiceClient(
            new Uri("https://testing.blob.core.windows.net"),
            new StorageSharedKeyCredential("testing", Convert.ToBase64String(new byte[32])));
        var resolver = new BlobMediaUrlResolver(client, NullLogger<BlobMediaUrlResolver>.Instance);

        var url = await resolver.ResolveTenantAsync(tenantId, "products/photo.png");

        Assert.StartsWith(
            $"https://testing.blob.core.windows.net/tenant-{tenantId:N}/products/photo.png?",
            url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_businesses_in_one_tenant_resolve_the_same_product_image_from_tenant_storage()
    {
        var tenantId = Guid.NewGuid();
        var firstBusinessId = Guid.NewGuid();
        var secondBusinessId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var mediaRef = $"products/{productId:N}/photo.png";
        var unitOfWork = CreateUnitOfWork(tenantId, productId, firstBusinessId, secondBusinessId);
        var image = new ProductImage
        {
            ProductImageId = Guid.NewGuid(), ProductId = productId, TenantId = tenantId,
            MediaUrl = mediaRef
        };
        unitOfWork.Products.Setup(repository => repository.GetImagesAsync(
                It.IsAny<Guid>(), productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([image]);
        var resolver = new Mock<IMediaUrlResolver>();
        resolver.Setup(value => value.ResolveTenantAsync(tenantId, mediaRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://example.test/photo.png");
        var service = new ProductOfferAdminService(
            unitOfWork.UnitOfWork.Object, Mock.Of<IBlobStorageService>(), resolver.Object);

        var first = await service.GetImagesAsync(tenantId, firstBusinessId, productId);
        var second = await service.GetImagesAsync(tenantId, secondBusinessId, productId);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].ProductImageId, second[0].ProductImageId);
        Assert.Equal(first[0].MediaUrl, second[0].MediaUrl);
        resolver.Verify(value => value.ResolveTenantAsync(tenantId, mediaRef, It.IsAny<CancellationToken>()), Times.Exactly(2));
        resolver.Verify(value => value.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Staging_from_either_business_uploads_to_the_same_tenant_container()
    {
        var tenantId = Guid.NewGuid();
        var firstBusinessId = Guid.NewGuid();
        var secondBusinessId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(tenantId, productId, firstBusinessId, secondBusinessId);
        var storage = new Mock<IBlobStorageService>();
        storage.Setup(value => value.UploadTenantImageAsync(
                tenantId, It.IsAny<Stream>(), It.Is<string>(name => name.StartsWith($"products/{productId:N}/"))))
            .ReturnsAsync("products/photo.png");
        var service = new ProductOfferAdminService(
            unitOfWork.UnitOfWork.Object, storage.Object, Mock.Of<IMediaUrlResolver>());

        await service.StageImageAsync(tenantId, firstBusinessId, productId, new MemoryStream([1]), "photo.png");
        await service.StageImageAsync(tenantId, secondBusinessId, productId, new MemoryStream([2]), "photo.png");

        storage.Verify(value => value.UploadTenantImageAsync(
            tenantId, It.IsAny<Stream>(), It.IsAny<string>()), Times.Exactly(2));
        storage.Verify(value => value.UploadImageAsync(
            It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Upload_from_second_business_saves_a_tenant_owned_image()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(tenantId, productId, businessId);
        ProductImage? saved = null;
        unitOfWork.Products.Setup(repository => repository.CreateImageAsync(
                It.IsAny<ProductImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductImage image, CancellationToken _) =>
            {
                saved = image;
                return image;
            });
        unitOfWork.UnitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var storage = new Mock<IBlobStorageService>();
        storage.Setup(value => value.UploadTenantImageAsync(
                tenantId, It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("products/new.png");
        var service = new ProductOfferAdminService(
            unitOfWork.UnitOfWork.Object, storage.Object, Mock.Of<IMediaUrlResolver>());

        await service.UploadImageAsync(
            tenantId, businessId, productId, null, new MemoryStream([1]), "new.png", null, false);

        Assert.NotNull(saved);
        Assert.Equal(tenantId, saved.TenantId);
        Assert.Equal("products/new.png", saved.MediaUrl);
        storage.Verify(value => value.UploadTenantImageAsync(
            tenantId, It.IsAny<Stream>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Selecting_a_primary_image_reads_the_product_images_once()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(tenantId, productId, businessId);
        var first = new ProductImage { ProductImageId = Guid.NewGuid(), ProductId = productId, TenantId = tenantId, IsPrimary = true };
        var second = new ProductImage { ProductImageId = Guid.NewGuid(), ProductId = productId, TenantId = tenantId };
        unitOfWork.Products.Setup(repository => repository.GetTrackedImagesAsync(
                businessId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        unitOfWork.UnitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = new ProductOfferAdminService(
            unitOfWork.UnitOfWork.Object, Mock.Of<IBlobStorageService>(), Mock.Of<IMediaUrlResolver>());

        await service.SetPrimaryImageAsync(tenantId, businessId, productId, second.ProductImageId);

        Assert.False(first.IsPrimary);
        Assert.True(second.IsPrimary);
        unitOfWork.Products.Verify(repository => repository.GetTrackedImagesAsync(
            businessId, productId, It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Products.Verify(repository => repository.GetImageByIdAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IProductRepository> Products) CreateUnitOfWork(
        Guid tenantId, Guid productId, params Guid[] businessIds)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var businesses = new Mock<IBusinessRepository>();
        businesses.Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid businessId) => businessIds.Contains(businessId)
                ? new Business { BusinessId = businessId, TenantId = tenantId }
                : null);
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByIdAsync(
                It.IsAny<Guid>(), productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { ProductId = productId, TenantId = tenantId });
        unitOfWork.SetupGet(value => value.Businesses).Returns(businesses.Object);
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        return (unitOfWork, products);
    }
}
