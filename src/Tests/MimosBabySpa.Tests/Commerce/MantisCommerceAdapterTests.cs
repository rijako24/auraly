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
    public async Task CreateOrderAsync_DefaultConfigurationCallsRealEndpoint()
    {
        const string orderJson = """{"ErrorKey":[],"EstadoPedido":"created","bPedNum":"OPHG003359"}""";
        var handler = new JsonHttpMessageHandler(orderJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","order":{"createEndpoint":"orders"}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var order = new Order { OrderId = Guid.NewGuid(), Notes = "Pedido de prueba" };
        var context = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            Customer: new CommerceCustomerReference(
                CommerceProvider.Mantis,
                "10013",
                "6826",
                "Cliente",
                "3001234567"),
            WarehouseCode: "1");

        var result = await adapter.CreateOrderAsync(
            order,
            [new OrderItem { Sku = "PV31", ProductNameSnapshot = "PAPA RIPIO KRUMER", Quantity = 1 }],
            context,
            CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        handler.LastRequestPath.Should().EndWith("/orders");
        handler.LastRequestJson.Should().Contain("\"CodigoArticulos\":\"PV31\"");
        result.ExternalOrderId.Should().Be("OPHG003359");
        result.ExternalStatus.Should().Be("created");
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
        handler.LastRequestJson.Should().Contain("\"Bodega\":\"\"");
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
    public async Task GetProductIdentityPageAsync_WhenMantisReturnsShortPage_DoesNotFanOutPerItem()
    {
        const string responseJson = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "P001",
                "NombreProducto": "PRODUCTO UNO",
                "MonedaProducto": "COP"
              }],
              "SDTPaginadoCasalins": {
                "NextPage": "True",
                "Page": 12,
                "PagaSize": 20,
                "TotalItems": 783
              }
            }
            """;
        var handler = new JsonHttpMessageHandler(responseJson);
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """{"baseUrl":"https://mantis.example/rest/","genericCustomer":{"llaveNit":"5702","llaveCliente":"1"},"catalog":{"searchEndpoint":"products","maxPageSize":20}}""");
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var context = new CommerceAdapterContext(
            businessId,
            Guid.Empty,
            null,
            CommerceProvider.Mantis,
            connection);

        var result = await ((ICommerceProductIdentitySource)adapter).GetProductIdentityPageAsync(
            context,
            page: 12,
            pageSize: 20,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        result.HasMore.Should().BeTrue();
        handler.RequestCount.Should().Be(1);
        handler.LastRequestJson.Should().Contain("\"Pagina\":12");
        handler.LastRequestJson.Should().Contain("\"CantPag\":20");
        handler.LastRequestJson.Should().Contain("\"Nomproducto\":\"\"");
        handler.LastRequestJson.Should().Contain("\"LlaveNit\":\"5702\"");
        handler.LastRequestJson.Should().Contain("\"LlaveCliente\":\"1\"");
        handler.LastRequestJson.Should().Contain("\"Bodega\":\"\"");
    }


    [Fact]
    public async Task SearchProductsAsync_UsesPersistedCustomerIdentityAcrossCalls()
    {
        const string customerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "CelularCliente": "3001234567",
                "LlaveNit": "10013",
                "LlaveCliente": 6826,
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
        var localCustomer = new ExternalCommerceCustomer
        {
            ExternalCommerceCustomerId = Guid.NewGuid(),
            BusinessId = businessId,
            IntegrationConnectionId = connection.IntegrationConnectionId,
            ExternalAccountId = "10013",
            ExternalCustomerId = "6826",
            Name = "Cliente especial",
            PhoneNormalized = "3001234567",
            Phone = "3001234567",
            IsActive = true
        };
        var customers = new Mock<IExternalCommerceCustomerRepository>();
        customers.SetupSequence(repository => repository.FindActiveByPhoneAsync(
                businessId,
                connection.IntegrationConnectionId,
                "3001234567",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([localCustomer]);
        customers.Setup(repository => repository.GetByExternalKeysAsync(
                businessId,
                connection.IntegrationConnectionId,
                "10013",
                "6826",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCommerceCustomer?)null);
        customers.Setup(repository => repository.CreateAsync(
                It.IsAny<ExternalCommerceCustomer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCommerceCustomer customer, CancellationToken _) => customer);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.ExternalCommerceCustomers).Returns(customers.Object);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
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
              "order": { "createEndpoint": "orders", "warehouse": "4" }
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
    public async Task SearchProductsAsync_WhenCustomerHasOnlyOneKey_UsesConfiguredGenericCustomer()
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
        var handler = new SequenceHttpMessageHandler(
            incompleteCustomerJson, incompleteCustomerJson, incompleteCustomerJson, incompleteCustomerJson,
            productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "genericCustomer": { "llaveNit": "5702", "llaveCliente": "1" },
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
            "3001234567",
            WarehouseCode: "2");

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5),
            ctx,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        handler.Requests.Should().HaveCount(5);
        handler.Requests[4].Json.Should().Contain("\"LlaveNit\":\"5702\"");
        handler.Requests[4].Json.Should().Contain("\"LlaveCliente\":\"1\"");
        handler.Requests[4].Json.Should().Contain("\"Bodega\":\"2\"");
    }

    [Fact]
    public async Task SearchProductsAsync_FindsCustomerByInternationalTelephoneFallback()
    {
        const string emptyJson = """{"ErrorKey":"","SDTConsultarClientesCasalins":[]}""";
        const string customerJson = """
            {
              "ErrorKey": "",
              "SDTConsultarClientesCasalins": [{
                "TelefonoClientes": "573001234567",
                "LlaveNit": "10013",
                "LlaveCliente": "6826"
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
                "PrecioProducto": "15000",
                "MonedaProducto": "COP"
              }]
            }
            """;
        var handler = new SequenceHttpMessageHandler(
            emptyJson, emptyJson, emptyJson, customerJson, productJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers", "countryCode": "57" },
              "catalog": { "searchEndpoint": "products", "cacheProducts": false }
            }
            """);
        var ctx = new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Mantis,
            connection,
            "+57 300 123 4567");

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5),
            ctx,
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        handler.Requests.Should().HaveCount(5);
        handler.Requests[3].Json.Should().Contain("\"CelCliente\":\"\"");
        handler.Requests[3].Json.Should().Contain("\"TelCliente\":\"573001234567\"");
        handler.Requests[4].Json.Should().Contain("\"LlaveNit\":\"10013\"");
        handler.Requests[4].Json.Should().Contain("\"LlaveCliente\":\"6826\"");
    }
    [Fact]
    public async Task CreateOrderAsync_WhenCustomerDoesNotExist_SendsConfiguredGenericCustomerAndWarehouse()
    {
        const string unknownCustomerJson = """{"ErrorKey":"","SDTConsultarClientesCasalins":[]}""";
        const string orderJson = """{"ErrorKey":[],"bPedNum":"APED-GENERIC-1"}""";
        var handler = new SequenceHttpMessageHandler(
            unknownCustomerJson, unknownCustomerJson, unknownCustomerJson, unknownCustomerJson,
            orderJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = CreateConnection(
            businessId,
            """
            {
              "baseUrl": "https://mantis.example/rest/",
              "customer": { "searchEndpoint": "customers" },
              "genericCustomer": { "llaveNit": "5702", "llaveCliente": "1" },
              "order": { "createEndpoint": "orders" }
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
            order.CustomerPhoneSnapshot,
            WarehouseCode: "2");

        var result = await adapter.CreateOrderAsync(
            order,
            [new OrderItem { Sku = "CF17", ProductNameSnapshot = "JAMON", Quantity = 1 }],
            ctx,
            CancellationToken.None);

        result.ExternalOrderId.Should().Be("APED-GENERIC-1");
        handler.Requests.Should().HaveCount(5);
        handler.Requests.Take(4).Should().OnlyContain(request => request.Path.EndsWith("/customers"));
        handler.Requests[4].Path.Should().EndWith("/orders");
        handler.Requests[4].Json.Should().Contain("\"LlaveNit\":\"5702\"");
        handler.Requests[4].Json.Should().Contain("\"LlaveCliente\":\"1\"");
        handler.Requests[4].Json.Should().Contain("\"bodega\":\"2\"");
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

    [Fact]
    public async Task GetProductAsync_WhenMantisReportsItemButOmitsArray_UsesActiveLocalIdentityForGenericCustomer()
    {
        const string responseJson = """
            {
              "ErrorKey": "",
              "SDTPaginadoCasalins": {
                "NextPage": "True",
                "PagaSize": 10,
                "Page": 1,
                "TotalItems": 1,
                "TotalPages": 0
              }
            }
            """;
        var businessId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = businessId,
            IntegrationConnectionId = connectionId,
            ExternalProductId = "CF16",
            Sku = "CF16",
            Name = "JAMON CUNICHEF X500GR",
            Currency = "COP",
            IsActive = true
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                businessId, connectionId, "CF16", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var handler = new JsonHttpMessageHandler(responseJson);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = connectionId,
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","catalog":{"searchEndpoint":"products","maxPageSize":20}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var context = new CommerceAdapterContext(
            businessId, Guid.Empty, null, CommerceProvider.Mantis, connection);

        var result = await adapter.GetProductAsync(
            new AddOrderItemRequest(product.ProductId, "CF16", "CF16", product.Name, 1m, null),
            context,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ProductId.Should().Be(product.ProductId);
        result.Sku.Should().Be("CF16");
        handler.LastRequestJson.Should().Contain("\"codproducto\":\"CF16\"");
    }

    private sealed class JsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonHttpMessageHandler(string json) => _json = json;

        public string? LastRequestJson { get; private set; }
        public string? LastRequestPath { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestPath = request.RequestUri?.AbsolutePath;
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
