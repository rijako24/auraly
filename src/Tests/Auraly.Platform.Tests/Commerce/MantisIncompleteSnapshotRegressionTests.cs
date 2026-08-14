using System.Net;
using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Commerce;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class MantisIncompleteSnapshotRegressionTests
{
    [Fact]
    public async Task AnonymousSearch_NeverSubstitutesLiveResponseWithCachedCommercialData()
    {
        const string responseJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "CF17",
                "DispProducto": "false",
                "ExiProducto": "0.00",
                "NombreProducto": "JAMON CUNIT X 500GR",
                "PrecioProducto": "0.00",
                "MonedaProducto": "COP"
              }]
            }
            """;
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","catalog":{"searchEndpoint":"products","cacheProducts":true}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var existing = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = businessId,
            IntegrationConnectionId = connection.IntegrationConnectionId,
            ExternalProductId = "CF17",
            Sku = "CF17",
            Name = "JAMON CUNIT X 500GR",
            UnitPrice = 18900m,
            Currency = "COP",
            ManageStock = true,
            StockQuantity = 12m,
            IsActive = true
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                businessId, connection.IntegrationConnectionId, "CF17", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var adapter = new MantisCommerceAdapter(
            new HttpClient(new StaticJsonHandler(responseJson)), unitOfWork.Object);

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5),
            new CommerceAdapterContext(
                businessId, Guid.NewGuid(), null, CommerceProvider.Mantis, connection),
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        result.Products[0].ProductId.Should().Be(existing.ProductId);
        result.Products[0].UnitPrice.Should().Be(0m);
        result.Products[0].StockQuantity.Should().Be(0m);
        result.Products[0].IsActive.Should().BeFalse();
        existing.UnitPrice.Should().Be(18900m);
        existing.StockQuantity.Should().Be(12m);
        existing.IsActive.Should().BeTrue();
        products.Verify(repository => repository.UpdateAsync(
            It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }
}
