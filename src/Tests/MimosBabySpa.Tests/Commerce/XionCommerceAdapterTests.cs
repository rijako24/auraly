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

public sealed class XionCommerceAdapterTests
{
    [Fact]
    public async Task SearchProductsAsync_UsesCustomerPriceAndWarehouseContext()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.Contains("ProductosABuscar/1/1/0/jamon/1/1/25") =>
                """[{"IdProducto":7,"DescripcionLarga":"JAMON"}]""",
            var path when path.Contains("InfoProducto/7/1/1/1/1/25") =>
                """{"IdProducto":7,"DescripcionLarga":"JAMON PREMIUM","DescripcionCorta":"JAMON","Existencias":18,"PrecioPublico1":12000,"InformacionVenta":{"PrecioVenta":9750}}""",
            _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri}")
        });
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), "7", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var adapter = new XionCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
        var context = Context(new CommerceCustomerReference(
            CommerceProvider.Xion, "900123", "25", "Cliente", "3001234567"));

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("jamon", null, 5), context);

        result.Products.Should().ContainSingle();
        result.Products[0].ExternalProductId.Should().Be("7");
        result.Products[0].UnitPrice.Should().Be(9750);
        result.Products[0].StockQuantity.Should().Be(18);
        handler.Paths.Should().Contain(path => path.Contains("ProductosABuscar/1/1/0/jamon/1/1/25"));
    }

    [Fact]
    public async Task SearchProductsAsync_BrowseMode_UsesConfiguredNativeBrowseValue()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.Contains("ProductosABuscarSinCliente/1/1/0/0/1/1") =>
                """[{"IdProducto":7,"DescripcionLarga":"JAMON"},{"IdProducto":8,"DescripcionLarga":"GALLETAS"}]""",
            var path when path.Contains("InfoProductoSinCliente/7/1/1/1/1") =>
                """{"IdProducto":7,"DescripcionLarga":"JAMON PREMIUM","Existencias":18,"PrecioPublico1":12000}""",
            var path when path.Contains("InfoProductoSinCliente/8/1/1/1/1") =>
                """{"IdProducto":8,"DescripcionLarga":"GALLETAS SURTIDAS","Existencias":10,"PrecioPublico1":8000}""",
            _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri}")
        });
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var adapter = new XionCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
        var context = ContextWithoutCustomer(
            SettingsJson());

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest(
                null, null, 2, Mode: ProductCatalogQueryMode.Browse),
            context);

        result.Products.Select(product => product.Name)
            .Should().Equal("JAMON PREMIUM", "GALLETAS SURTIDAS");
        handler.Paths.Should().Contain(path =>
            path.Contains("ProductosABuscarSinCliente/1/1/0/0/1/1"));
    }

    [Fact]
    public async Task SearchProductsAsync_ConcreteQuery_NeverUsesNativeBrowseValue()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.Contains("ProductosABuscarSinCliente/1/1/0/jamon/1/1") =>
                """[{"IdProducto":7,"DescripcionLarga":"JAMON"}]""",
            var path when path.Contains("InfoProductoSinCliente/7/1/1/1/1") =>
                """{"IdProducto":7,"DescripcionLarga":"JAMON PREMIUM","Existencias":18,"PrecioPublico1":12000}""",
            _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri}")
        });
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByExternalIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var adapter = new XionCommerceAdapter(new HttpClient(handler), unitOfWork.Object);
        var context = ContextWithoutCustomer(SettingsJson());

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest(
                "jamon", null, 2, Mode: ProductCatalogQueryMode.Browse),
            context);

        result.Products.Should().ContainSingle();
        handler.Paths.Should().Contain(path =>
            path.Contains("ProductosABuscarSinCliente/1/1/0/jamon/1/1"));
        handler.Paths.Should().NotContain(path =>
            path.Contains("ProductosABuscarSinCliente/1/1/0/0/1/1"));
    }

    [Fact]
    public async Task CreateOrderAsync_GeneratesConsecutivePostsAndVerifiesOrder()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("SiguienteConsecutivo/1"))
                return "12";
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("Nuevo/Pedido/true"))
                return "[]";
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("VerificarPedido/PE00100000012"))
                return "true";
            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        });
        var adapter = new XionCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var context = Context(new CommerceCustomerReference(
            CommerceProvider.Xion, "900123", "25", "Cliente", "3001234567"));
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerNameSnapshot = "Cliente",
            Total = 19500,
            TaxTotal = 0
        };
        var item = new OrderItem
        {
            ExternalProductId = "7",
            ProductNameSnapshot = "JAMON PREMIUM",
            Quantity = 2,
            UnitPrice = 9750,
            LineTotal = 19500,
            RawPayloadJson = """{"IdProducto":7,"DescripcionLarga":"JAMON PREMIUM","DescripcionCorta":"JAMON","PrecioCosto":7000,"InformacionVenta":{"PrecioVenta":9750,"PrecioCosto":7000}}"""
        };

        var result = await adapter.CreateOrderAsync(order, [item], context);

        result.ExternalOrderId.Should().Be("PE00100000012");
        handler.LastPostJson.Should().Contain("\"pedidoId\":\"PE00100000012\"");
        handler.LastPostJson.Should().Contain("\"idCliente\":25");
        handler.LastPostJson.Should().Contain("\"bodegaId\":1");
        handler.Paths.Should().Contain(path => path.Contains("VerificarPedido/PE00100000012"));
    }

    private static CommerceAdapterContext Context(CommerceCustomerReference customer)
    {
        var businessId = Guid.NewGuid();
        return new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Xion,
            new IntegrationConnection
            {
                IntegrationConnectionId = Guid.NewGuid(),
                BusinessId = businessId,
                ConnectionType = ConnectionType.Commerce,
                Provider = (int)CommerceProvider.Xion,
                Capability = (int)CommerceCapability.CatalogAndOrders,
                IsEnabled = true,
                SettingsJson = SettingsJson()
            },
            Customer: customer);
    }

    private static string SettingsJson() =>
        """{"baseUrl":"https://xion.example/","currency":"COP","requestTimeoutSeconds":120,"sucursalId":1,"vendedorId":1,"equipoId":1,"bodegaId":1,"empresaId":1,"centroDeCostoId":1,"usuarioId":1,"rutaId":0,"validateStockOnCreate":true,"orderHistoryDays":365,"catalogBrowseSearchValue":"0","endpoints":{"customerSync":"WebApi/Vendedores/Sync/Clientes/{vendedorId}/{sucursalId}","productSearch":"WebApi/Vendedores/Consulta/ProductosABuscar/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}/{clienteId}","productSearchWithoutCustomer":"WebApi/Vendedores/Consulta/ProductosABuscarSinCliente/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}","productDetail":"WebApi/Vendedores/Consulta/InfoProducto/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}/{clienteId}","productDetailWithoutCustomer":"WebApi/Vendedores/Consulta/InfoProductoSinCliente/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}","productSync":"WebApi/Vendedores/Sync/Productos/{vendedorId}/{sucursalId}","nextOrderNumber":"WebApi/Vendedores/Consulta/Pedido/SiguienteConsecutivo/{equipoId}","createOrder":"WebApi/Vendedores/Nuevo/Pedido/{validarExistencia}","orderHistory":"WebApi/Vendedores/Consulta/Pedidos/{vendedorId}/{fechaInicial}/{fechaFin}/{clienteId}/{rutaId}/{criterio}","verifyOrder":"WebApi/Vendedores/Consulta/VerificarPedido/{pedidoId}"}}""";
    private static CommerceAdapterContext ContextWithoutCustomer(string settingsJson)
    {
        var businessId = Guid.NewGuid();
        return new CommerceAdapterContext(
            businessId,
            Guid.NewGuid(),
            null,
            CommerceProvider.Xion,
            new IntegrationConnection
            {
                IntegrationConnectionId = Guid.NewGuid(),
                BusinessId = businessId,
                ConnectionType = ConnectionType.Commerce,
                Provider = (int)CommerceProvider.Xion,
                Capability = (int)CommerceCapability.CatalogAndOrders,
                IsEnabled = true,
                SettingsJson = settingsJson
            },
            Customer: null);
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? LastPostJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Post)
                LastPostJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request))
            };
        }
    }
}