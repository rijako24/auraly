using System.Net;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class MantisCommerceAdapterTests
{
    [Fact]
    public async Task CreateOrderAsync_ReturnsMockedResultWithoutCallingMantis()
    {
        var handler = new FailingHttpMessageHandler();
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerNameSnapshot = "Richard",
            CustomerPhoneSnapshot = "3012926660",
            DeliveryAddressSnapshot = "Conjunto Barcelona",
            Total = 123000m
        };
        var items = new[]
        {
            new OrderItem
            {
                Sku = "SKU-1",
                ProductNameSnapshot = "Arroz",
                Quantity = 2
            }
        };
        var ctx = new CommerceAdapterContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConversationId: null,
            CommerceProvider.Mantis,
            Connection: null);

        var result = await adapter.CreateOrderAsync(order, items, ctx, CancellationToken.None);

        handler.RequestCount.Should().Be(0);
        result.ExternalOrderId.Should().StartWith("mantis-mock-");
        result.ExternalStatus.Should().Be("mocked");
        result.ResponseJson.Should().Contain("\"mode\":\"mock\"");
    }


    [Fact]
    public async Task SearchProductsAsync_MapsMantisContractToProductReference()
    {
        const string responseJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [
                {
                  "CategoriaProducto": "CARNE DE POLLO",
                  "ClaseProducto": "General",
                  "CodigoProducto": "PO08",
                  "DispProducto": "true",
                  "ExiProducto": "4606.82",
                  "FamiliaProducto": "PECHUGA",
                  "MonedaProducto": "COP",
                  "NombreProducto": "PECHUGA MAC POLLO",
                  "PrecioProducto": "13001.08",
                  "PresProducto": "GRAMOS",
                  "SubCategoriaProducto": "GENERAL",
                  "TipoProducto": "I"
                }
              ],
              "SDTPaginadoCasalins": { "NextPage": "False", "Page": 1, "PagaSize": 5 }
            }
            """;
        var handler = new JsonHttpMessageHandler(responseJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = "{\"baseUrl\":\"https://mantis.example/rest/\",\"catalog\":{\"searchEndpoint\":\"products\",\"cacheProducts\":false}}",
            SecretsJson = "{\"authorizationToken\":\"token\"}"
        };
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            ConversationId: null,
            CommerceProvider.Mantis,
            connection);

        var result = await adapter.SearchProductsAsync(new ProductSearchRequest("pechuga", null, 5), ctx, CancellationToken.None);

        result.Products.Should().ContainSingle();
        handler.LastRequestJson.Should().Contain("\"CantPag\":5");
        var product = result.Products[0];
        product.ExternalProductId.Should().Be("PO08");
        product.Sku.Should().Be("PO08");
        product.Name.Should().Be("PECHUGA MAC POLLO");
        product.Description.Should().Be("GRAMOS");
        product.CategoryName.Should().Be("CARNE DE POLLO");
        product.FamilyName.Should().Be("PECHUGA");
        product.SubcategoryName.Should().Be("GENERAL");
        product.ProductClassName.Should().Be("General");
        product.UnitPrice.Should().Be(13001.08m);
        product.StockQuantity.Should().Be(4606.82m);
        product.IsActive.Should().BeTrue();
        product.RawPayloadJson.Should().Contain("PECHUGA MAC POLLO");
    }


    private sealed class JsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonHttpMessageHandler(string json) => _json = json;

        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };
        }
    }
    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}