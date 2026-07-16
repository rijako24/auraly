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
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","order":{"mockCreateOrders":true}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            ConversationId: null,
            CommerceProvider.Mantis,
            Connection: connection);

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
        handler.LastRequestJson.Should().Contain("\"LlaveNit\":\"\"");
        handler.LastRequestJson.Should().Contain("\"LlaveCliente\":\"\"");
        handler.LastRequestJson.Should().Contain("\"Bodega\":\"1\"");
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


    [Fact]
    public async Task SearchProductsAsync_UsesKnownCustomerKeysAndCachesLookupWithinScope()
    {
        const string customerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "CelularCliente": "3001234567",
                "LlaveNit": "10013",
                "LlaveCliente": "6826",
                "NombreCliente": "Cliente especial"
              }]
            }
            """;
        const string productJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "PO08",
                "DispProducto": "true",
                "NombreProducto": "PECHUGA MAC POLLO",
                "PrecioProducto": "11900",
                "MonedaProducto": "COP"
              }],
              "SDTPaginadoCasalins": { "NextPage": "False", "Page": 1, "PagaSize": 5 }
            }
            """;
        var handler = new SequenceHttpMessageHandler(customerJson, productJson, productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers", "countryCode": "57" },
              "catalog": { "searchEndpoint": "products", "cacheProducts": false, "warehouse": "7" }
            }
            """);
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            "+57 (300) 123-4567");

        var first = await adapter.SearchProductsAsync(
            new ProductSearchRequest("pechuga", null, 5),
            ctx,
            CancellationToken.None);
        var second = await adapter.SearchProductsAsync(
            new ProductSearchRequest("pechuga", null, 5),
            ctx,
            CancellationToken.None);

        first.Products.Should().ContainSingle();
        first.Products[0].UnitPrice.Should().Be(11900m);
        second.Products.Should().ContainSingle();
        handler.Requests.Should().HaveCount(3);
        handler.Requests.Count(request => request.Path.EndsWith("/customers", StringComparison.Ordinal)).Should().Be(1);
        handler.Requests[0].Json.Should().Contain("\"CantPag\":1");
        handler.Requests[0].Json.Should().Contain("\"CelCliente\":\"3001234567\"");
        handler.Requests[1].Json.Should().Contain("\"LlaveNit\":\"10013\"");
        handler.Requests[1].Json.Should().Contain("\"LlaveCliente\":\"6826\"");
        handler.Requests[1].Json.Should().Contain("\"Bodega\":\"7\"");
        handler.Requests[2].Json.Should().Contain("\"LlaveNit\":\"10013\"");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenRealMode_SendsResolvedKeysAndParsesOrderNumber()
    {
        const string customerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "CelularCliente": "3001234567",
                "LlaveNit": "10013",
                "LlaveCliente": "6826"
              }]
            }
            """;
        const string orderJson = """{"ErrorKey":[],"bPedNum":"APED000123"}""";
        var handler = new SequenceHttpMessageHandler(customerJson, orderJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "order": { "createEndpoint": "orders", "warehouse": "4", "mockCreateOrders": false }
            }
            """);
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerPhoneSnapshot = "573001234567",
            Notes = "Pedido WhatsApp"
        };
        var items = new[]
        {
            new OrderItem { Sku = "CF17", ProductNameSnapshot = "JAMON CUNIT X 500GR", Quantity = 2.5m }
        };
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            order.CustomerPhoneSnapshot);

        var result = await adapter.CreateOrderAsync(order, items, ctx, CancellationToken.None);

        result.ExternalOrderId.Should().Be("APED000123");
        result.ExternalStatus.Should().Be("created");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Path.Should().EndWith("/orders");
        handler.Requests[1].Json.Should().Contain("\"LlaveNit\":\"10013\"");
        handler.Requests[1].Json.Should().Contain("\"LlaveCliente\":\"6826\"");
        handler.Requests[1].Json.Should().Contain("\"CodigoArticulos\":\"CF17\"");
        handler.Requests[1].Json.Should().Contain("\"CantidadArticulos\":\"2.5\"");
        handler.Requests[1].Json.Should().Contain("\"bodega\":\"4\"");
    }

    [Fact]
    public async Task SearchProductsAsync_DoesNotPersistCustomerSpecificPriceAsBasePrice()
    {
        const string customerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "CelularCliente": "3001234567",
                "LlaveNit": "10013",
                "LlaveCliente": "6826"
              }]
            }
            """;
        const string productJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "PO08",
                "DispProducto": "true",
                "NombreProducto": "PECHUGA MAC POLLO",
                "PrecioProducto": "11900",
                "MonedaProducto": "COP"
              }]
            }
            """;
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "catalog": { "searchEndpoint": "products", "cacheProducts": true }
            }
            """);
        var cachedProduct = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = businessId,
            IntegrationConnectionId = connection.IntegrationConnectionId,
            ExternalProductId = "PO08",
            Name = "PECHUGA MAC POLLO",
            UnitPrice = 13001.08m
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                businessId,
                connection.IntegrationConnectionId,
                "PO08",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedProduct);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var handler = new SequenceHttpMessageHandler(customerJson, productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            "3001234567");

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("pechuga", null, 5),
            ctx,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        result.Products[0].ProductId.Should().Be(cachedProduct.ProductId);
        result.Products[0].UnitPrice.Should().Be(11900m);
        cachedProduct.UnitPrice.Should().Be(13001.08m);
        products.Verify(repository => repository.UpdateAsync(
            It.IsAny<Product>(),
            It.IsAny<CancellationToken>()), Times.Never);
        products.Verify(repository => repository.CreateAsync(
            It.IsAny<Product>(),
            It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchProductsAsync_UsesTurnCustomerWithoutRepeatingCustomerLookup()
    {
        const string productJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "CF17",
                "DispProducto": "true",
                "NombreProducto": "JAMON CUNIT X 500GR",
                "PrecioProducto": "18900",
                "MonedaProducto": "COP"
              }]
            }
            """;
        var handler = new SequenceHttpMessageHandler(productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "catalog": { "searchEndpoint": "products", "cacheProducts": false }
            }
            """);
        var customer = new CommerceCustomerReference(
            CommerceProvider.Mantis,
            "10013",
            "6826",
            "Claudia",
            "3001234567");
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            "573001234567",
            customer);

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5),
            ctx,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().EndWith("/products");
        handler.Requests[0].Json.Should().Contain("\"LlaveNit\":\"10013\"");
        handler.Requests[0].Json.Should().Contain("\"LlaveCliente\":\"6826\"");
    }

    [Fact]
    public async Task SearchProductsAsync_WhenCustomerHasOnlyOneKey_UsesBasePriceContract()
    {
        const string incompleteCustomerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "CelularCliente": "3001234567",
                "LlaveNit": "10013",
                "LlaveCliente": ""
              }]
            }
            """;
        const string productJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "CF17",
                "DispProducto": "true",
                "NombreProducto": "JAMON CUNIT X 500GR",
                "PrecioProducto": "20000"
              }]
            }
            """;
        var handler = new SequenceHttpMessageHandler(incompleteCustomerJson, productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "catalog": { "searchEndpoint": "products", "cacheProducts": false }
            }
            """);
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            "3001234567");

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5),
            ctx,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Json.Should().Contain("\"LlaveNit\":\"\"");
        handler.Requests[1].Json.Should().Contain("\"LlaveCliente\":\"\"");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenCustomerDoesNotExist_DoesNotSendOrder()
    {
        const string unknownCustomerJson = """{"ErrorKey":"","SDTConsultarClientesCasalins":[]}""";
        var handler = new SequenceHttpMessageHandler(unknownCustomerJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "order": { "createEndpoint": "orders", "mockCreateOrders": false }
            }
            """);
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerPhoneSnapshot = "3001234567"
        };
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            order.CustomerPhoneSnapshot);

        var action = () => adapter.CreateOrderAsync(
            order,
            [new OrderItem { Sku = "CF17", ProductNameSnapshot = "JAMON", Quantity = 1 }],
            ctx,
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*customer was not found*");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().EndWith("/customers");
    }

    private static IntegrationConnection CreateConnection(Guid businessId, string settingsJson) =>
        new()
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = settingsJson,
            SecretsJson = """{"authorizationToken":"token"}"""
        };

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public SequenceHttpMessageHandler(params string[] responses) =>
            _responses = new Queue<string>(responses);

        public List<(string Path, string Json)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No HTTP response was configured.");

            var json = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, json));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue())
            };
        }
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