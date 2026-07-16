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

public sealed class MantisOmittedArrayFallbackTests
{
    [Fact]
    public async Task SearchProducts_WhenTotalIsReportedButArrayIsMissing_ReadsSingleItemPages()
    {
        var handler = new SequenceHandler(
            """{"ErrorKey":"","SDTPaginadoCasalins":{"TotalItems":2}}""",
            ProductPage("CG40", "PAPA RIPIO KRUMER"),
            ProductPage("CG41", "PAPA RIPIO PREMIUM 1K"));
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","catalog":{"searchEndpoint":"products","cacheProducts":false,"maxPageSize":5}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var adapter = new MantisCommerceAdapter(
            new HttpClient(handler), Mock.Of<IUnitOfWork>());

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("ripio", null, 5),
            new CommerceAdapterContext(
                businessId, Guid.NewGuid(), null, CommerceProvider.Mantis, connection),
            CancellationToken.None);

        result.Products.Select(product => product.Name).Should().BeEquivalentTo([
            "PAPA RIPIO KRUMER", "PAPA RIPIO PREMIUM 1K"]);
        handler.RequestBodies.Should().HaveCount(3);
        handler.RequestBodies[1].Should().Contain("\"Pagina\":1").And.Contain("\"CantPag\":1");
        handler.RequestBodies[2].Should().Contain("\"Pagina\":2").And.Contain("\"CantPag\":1");
    }

    private static string ProductPage(string code, string name) => $$"""
        {
          "ErrorKey": "",
          "SDTConArtCasalins": [{
            "CodigoProducto": "{{code}}",
            "NombreProducto": "{{name}}",
            "DispProducto": "false",
            "PrecioProducto": "0.00",
            "ExiProducto": "0.00"
          }],
          "SDTPaginadoCasalins": { "TotalItems": 2 }
        }
        """;

    private sealed class SequenceHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue())
            };
        }
    }
}
