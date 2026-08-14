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

public sealed class MantisOmittedArrayFallbackTests
{
    [Fact]
    public async Task SearchProducts_WhenReportedPageIsIncomplete_ReadsSingleItemPages()
    {
        var handler = new SequenceHandler(
            ProductPage("CG40", "PAPA RIPIO KRUMER"),
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

    [Fact]
    public async Task SearchProducts_WhenNextPageIsTrue_ContinuesDespiteShortPage()
    {
        const string response = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "CF17",
                "NombreProducto": "JAMON CUNIT X 500GR",
                "DispProducto": "true",
                "PrecioProducto": "18900"
              }],
              "SDTPaginadoCasalins": { "NextPage": "True", "Page": 1, "PagaSize": 20 }
            }
            """;
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","catalog":{"searchEndpoint":"products","maxPageSize":20}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var handler = new SequenceHandler(response);
        var adapter = new MantisCommerceAdapter(
            new HttpClient(handler), Mock.Of<IUnitOfWork>());

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest(null, null, 20),
            new CommerceAdapterContext(
                businessId, Guid.NewGuid(), null, CommerceProvider.Mantis, connection));
        handler.RequestBodies[0].Should().Contain("\"Nomproducto\":\"\"");

        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task SearchProducts_WhenMantisIsTransientlyUnavailable_RetriesPage()
    {
        const string response = """
            {
              "ErrorKey": "",
              "SDTConArtCasalins": [{
                "CodigoProducto": "CF17",
                "NombreProducto": "JAMON CUNIT X 500GR",
                "DispProducto": "true",
                "PrecioProducto": "18900"
              }],
              "SDTPaginadoCasalins": { "NextPage": "False" }
            }
            """;
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            SettingsJson = """{"baseUrl":"https://mantis.example/rest/","requestTimeoutSeconds":2,"catalog":{"searchEndpoint":"products"}}""",
            SecretsJson = """{"authorizationToken":"token"}"""
        };
        var handler = new TransientThenSuccessHandler(response);
        var adapter = new MantisCommerceAdapter(new HttpClient(handler), Mock.Of<IUnitOfWork>());

        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest("CF17", null, 1),
            new CommerceAdapterContext(
                businessId, Guid.NewGuid(), null, CommerceProvider.Mantis, connection));

        result.Products.Should().ContainSingle();
        handler.RequestCount.Should().Be(2);
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

    private sealed class TransientThenSuccessHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
