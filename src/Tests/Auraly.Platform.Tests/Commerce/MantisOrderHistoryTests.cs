using System.Net;
using FluentAssertions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Commerce;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class MantisOrderHistoryTests
{
    [Fact]
    public async Task GetOrderHistoryAsync_MapsHeaderItemsAndFilters()
    {
        const string responseJson = """
            {
              "ErrorKey": null,
              "SDTConsultarPedidoCasalins": [{
                "FechaPedido": "2026-07-17",
                "IdentificacionCliente": "900000001",
                "NombreCliente": "Cliente de prueba",
                "NumeroPedido": "OPHG003359",
                "SDTConsultarPedidoCasalinsDetalle": [{
                  "CodigoProducto": "PV31",
                  "NombrePresentacion": "UNIDADES",
                  "NombreProducto": "PAPA RIPIO KRUMER",
                  "Precio": "10099.7800",
                  "Unidades": "2.00"
                }]
              }]
            }
            """;
        var handler = new CaptureHandler(responseJson);
        var adapter = new MantisCommerceAdapter(
            new HttpClient(handler),
            Mock.Of<IUnitOfWork>());
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """
                {
                  "baseUrl": "https://mantis.example/rest/",
                  "order": { "queryEndpoint": "query-orders" }
                }
                """,
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var context = new CommerceAdapterContext(
            businessId,
            Guid.Empty,
            null,
            CommerceProvider.Mantis,
            connection);

        var orders = await adapter.GetOrderHistoryAsync(
            context,
            new CommerceOrderHistoryQuery(
                "OPHG003359",
                "900000001",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 17)),
            CancellationToken.None);

        var order = orders.Should().ContainSingle().Subject;
        order.ExternalOrderId.Should().Be("OPHG003359");
        order.ExternalCustomerLookupId.Should().Be("900000001");
        order.OrderedOn.Should().Be(new DateOnly(2026, 7, 17));
        order.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CommerceOrderHistoryItem(
                "PV31",
                "PAPA RIPIO KRUMER",
                "UNIDADES",
                2m,
                10099.78m));
        handler.RequestPath.Should().EndWith("/query-orders");
        handler.RequestJson.Should().Contain("\"NroPedido\":\"OPHG003359\"");
        handler.RequestJson.Should().Contain("\"IdeClientes\":\"900000001\"");
        handler.RequestJson.Should().Contain("\"FechaInicial\":\"2026-07-01\"");
        handler.RequestJson.Should().Contain("\"FechaFinal\":\"2026-07-17\"");
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestPath { get; private set; }
        public string? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestJson = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }
}
