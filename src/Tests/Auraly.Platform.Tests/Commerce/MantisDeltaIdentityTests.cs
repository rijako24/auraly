using System.Net;
using System.Text;
using FluentAssertions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Commerce;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class MantisDeltaIdentityTests
{
    [Fact]
    public async Task DeltaPage_SendsModificationDate_AndTreatsMissingArrayAsEmpty()
    {
        var handler = new CaptureHandler(
            """{"ErrorKey":"","SDTPaginadoCasalins":{"NextPage":"False","Page":1,"PagaSize":50,"TotalItems":0,"TotalPages":1}}""");
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","genericCustomer":{"llaveNit":"5702","llaveCliente":"1"},"catalog":{"searchEndpoint":"products","maxPageSize":50}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());
        var context = new CommerceAdapterContext(
            connection.BusinessId, Guid.Empty, null, CommerceProvider.Mantis, connection);

        var result = await ((ICommerceProductDeltaIdentitySource)adapter)
            .GetProductIdentityDeltaPageAsync(
                context, new DateTime(2026, 1, 27, 15, 30, 0, DateTimeKind.Utc), 1, 50);

        result.Products.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        handler.Body.Should().Contain("\"FechaCreacion\":\"\"");
        handler.Body.Should().Contain("\"FechaModificacion\":\"2026-01-27\"");
        handler.Body.Should().Contain("\"LlaveNit\":\"5702\"");
        handler.Body.Should().Contain("\"LlaveCliente\":\"1\"");
    }

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
