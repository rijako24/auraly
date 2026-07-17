using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductCatalogSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_PersistsOnlyStableIdentityAndBuildsSearchIndex()
    {
        var fixture = new SyncFixture();
        Product? created = null;
        fixture.Products.Setup(repository => repository.GetByExternalIdAsync(
                fixture.BusinessId,
                fixture.Connection.IntegrationConnectionId,
                "CF17",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        fixture.Products.Setup(repository => repository.CreateAsync(
                It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((product, _) => created = product)
            .ReturnsAsync((Product product, CancellationToken _) => product);
        fixture.Adapter.Setup(adapter => adapter.SearchProductsAsync(
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                new ProductReference(
                    null, "CF17", "CF17", "JAMON CUNIT X 500GR",
                    "GRAMOS", "CARNES", 18_900m, "COP", 12m,
                    RawPayloadJson: """{"PrecioProducto":"18900"}""",
                    FamilyName: "JAMONES",
                    SubcategoryName: "REFRIGERADOS")
                { IsActive = false }
            ], "mantis"));

        var result = await fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(Provider: CommerceProvider.Mantis));

        result.ProductsProcessed.Should().Be(1);
        result.ProductsChanged.Should().Be(1);
        created.Should().NotBeNull();
        created!.Name.Should().Be("JAMON CUNIT X 500GR");
        created.Description.Should().ContainAll("GRAMOS", "JAMONES", "REFRIGERADOS");
        created.UnitPrice.Should().Be(0m);
        created.ManageStock.Should().BeFalse();
        created.StockQuantity.Should().BeNull();
        created.RawPayloadJson.Should().BeNull();
        created.IsActive.Should().BeTrue();
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(
            created, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenIdentityIsUnchanged_PerformsNoProductWrite()
    {
        var fixture = new SyncFixture();
        var existing = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = fixture.BusinessId,
            IntegrationConnectionId = fixture.Connection.IntegrationConnectionId,
            ExternalProductId = "CF17",
            Sku = "CF17",
            Name = "JAMON CUNIT X 500GR",
            Description = "GRAMOS JAMONES REFRIGERADOS",
            CategoryName = "CARNES",
            UnitPrice = 0m,
            Currency = "COP",
            ManageStock = false,
            StockQuantity = null,
            IsActive = true,
            RawPayloadJson = null,
            SearchIndexVersion = 2
        };
        fixture.Products.Setup(repository => repository.GetByExternalIdAsync(
                fixture.BusinessId,
                fixture.Connection.IntegrationConnectionId,
                "CF17",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        fixture.Adapter.Setup(adapter => adapter.SearchProductsAsync(
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                new ProductReference(
                    null, "CF17", "CF17", "JAMON CUNIT X 500GR",
                    "GRAMOS", "CARNES", 99_999m, "COP", 0m,
                    FamilyName: "JAMONES",
                    SubcategoryName: "REFRIGERADOS")
            ], "mantis"));

        var result = await fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(Provider: CommerceProvider.Mantis));

        result.ProductsChanged.Should().Be(0);
        fixture.Products.Verify(repository => repository.UpdateAsync(
            It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Products.Verify(repository => repository.ReplaceSearchTermsAsync(
            It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_AfterInterruptedPage_ResumesFromPersistedCheckpoint()
    {
        var fixture = new SyncFixture();
        var requestedPages = new List<int>();
        fixture.Products.Setup(repository => repository.GetByExternalIdAsync(
                fixture.BusinessId,
                fixture.Connection.IntegrationConnectionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        fixture.Products.Setup(repository => repository.CreateAsync(
                It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product product, CancellationToken _) => product);
        fixture.Adapter.Setup(adapter => adapter.SearchProductsAsync(
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductSearchRequest, CommerceAdapterContext, CancellationToken>(
                (request, _, _) => requestedPages.Add(request.Page))
            .ReturnsAsync((ProductSearchRequest request, CommerceAdapterContext _, CancellationToken _) =>
                new ProductSearchResult([
                    new ProductReference(
                        null,
                        $"P{request.Page}",
                        $"P{request.Page}",
                        $"PRODUCT {request.Page}",
                        null,
                        null,
                        0m,
                        "COP",
                        null)
                ], "mantis") { HasMore = request.Page == 1 });

        var firstAttempt = () => fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(PageSize: 20, MaxPages: 1, Provider: CommerceProvider.Mantis));

        await firstAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resume from the saved checkpoint*");
        fixture.Connection.CatalogSyncNextPage.Should().Be(2);
        fixture.Connection.LastSyncAt.Should().BeNull();

        var completed = await fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(PageSize: 20, MaxPages: 5, Provider: CommerceProvider.Mantis));

        requestedPages.Should().Equal(1, 2);
        completed.PagesProcessed.Should().Be(1);
        fixture.Connection.CatalogSyncNextPage.Should().Be(1);
        fixture.Connection.CustomerSyncNextPage.Should().Be(1);
        fixture.Connection.LastSyncAt.Should().NotBeNull();
        fixture.Connection.LastError.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_WithPriorSuccessfulSync_UsesOverlappingDateDeltaAndClearsCheckpoint()
    {
        var fixture = new SyncFixture();
        fixture.Connection.LastSyncAt = DateTime.UtcNow.Date;
        var requestedDates = new List<DateTime>();
        fixture.DeltaAdapter
            .Setup(source => source.GetProductIdentityDeltaPageAsync(
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<CommerceAdapterContext, DateTime, int, int, CancellationToken>(
                (_, date, _, _, _) => requestedDates.Add(date))
            .ReturnsAsync(new ProductIdentityPage([], false));

        var result = await fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(Provider: CommerceProvider.Mantis));

        requestedDates.Should().Equal(
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date);
        result.PagesProcessed.Should().Be(2);
        result.ProductsProcessed.Should().Be(0);
        fixture.Connection.CatalogSyncNextPage.Should().Be(1);
        fixture.Connection.CatalogDeltaCursorDate.Should().BeNull();
        fixture.Connection.LastError.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_WhenAdapterIgnoresCancellation_StopsAtConfiguredPageTimeout()
    {
        var fixture = new SyncFixture();
        var pending = new TaskCompletionSource<ProductSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Setup(adapter => adapter.SearchProductsAsync(
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(pending.Task);

        var action = () => fixture.Service.SyncAsync(
            fixture.BusinessId,
            new ProductCatalogSyncRequest(Provider: CommerceProvider.Mantis, PageTimeoutSeconds: 1));

        await action.Should().ThrowAsync<TimeoutException>().WithMessage("Catalog page 1 exceeded*");
        fixture.Connection.CatalogSyncNextPage.Should().Be(1);
        fixture.Connection.LastError.Should().Contain("Catalog page 1 exceeded");
    }


    private sealed class SyncFixture
    {
        public Guid BusinessId { get; } = Guid.NewGuid();
        public IntegrationConnection Connection { get; }
        public Mock<IProductRepository> Products { get; } = new();
        public Mock<ICommerceAdapter> Adapter { get; } = new();
        public ProductCatalogSyncService Service { get; }

        public Mock<ICommerceProductDeltaIdentitySource> DeltaAdapter { get; }
        public SyncFixture()
        {
            DeltaAdapter = Adapter.As<ICommerceProductDeltaIdentitySource>();
            Connection = new IntegrationConnection
            {
                IntegrationConnectionId = Guid.NewGuid(),
                BusinessId = BusinessId,
                ConnectionType = ConnectionType.Commerce,
                Provider = (int)CommerceProvider.Mantis,
                Capability = (int)CommerceCapability.CatalogAndOrders,
                IsEnabled = true
            };
            var connections = new Mock<IIntegrationConnectionRepository>();
            connections.Setup(repository => repository.GetByBusinessConnectionTypeAsync(
                    BusinessId, ConnectionType.Commerce, It.IsAny<CancellationToken>()))
                .ReturnsAsync([Connection]);
            connections.Setup(repository => repository.UpdateAsync(
                    It.IsAny<IntegrationConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IntegrationConnection value, CancellationToken _) => value);
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(connections.Object);
            unitOfWork.SetupGet(value => value.Products).Returns(Products.Object);
            unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            var factory = new Mock<ICommerceAdapterFactory>();
            factory.Setup(value => value.Resolve(CommerceProvider.Mantis)).Returns(Adapter.Object);
            Service = new ProductCatalogSyncService(unitOfWork.Object, factory.Object);
        }
    }
}
