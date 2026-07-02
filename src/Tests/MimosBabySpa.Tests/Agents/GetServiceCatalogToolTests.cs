using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class GetServiceCatalogToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsCatalogWithoutParameters()
    {
        var businessId = Guid.NewGuid();
        var catalog = new FakeCatalogGenerator(businessId, "## CATALOGO DE SERVICIOS\n- Plan Test");
        var tool = new GetServiceCatalogTool(catalog);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("Plan Test");
        json.Should().Contain("\"view\":\"services\"");
        catalog.WasCalled.Should().BeTrue();
        catalog.RequestedView.Should().Be(CatalogContentView.Services);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsCategoriesView()
    {
        var businessId = Guid.NewGuid();
        var catalog = new FakeCatalogGenerator(businessId, "## CATEGORIAS DE SERVICIOS\n- Corte");
        var tool = new GetServiceCatalogTool(catalog);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("""{"view":"categories"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("CATEGORIAS DE SERVICIOS");
        json.Should().Contain("\"view\":\"categories\"");
        catalog.RequestedView.Should().Be(CatalogContentView.Categories);
    }

    private sealed class FakeCatalogGenerator(Guid businessId, string content) : ICatalogContentGenerator
    {
        public bool WasCalled { get; private set; }
        public CatalogContentView? RequestedView { get; private set; }

        public Task<string> GenerateAsync(Guid requestedBusinessId, CancellationToken ct = default) =>
            GenerateAsync(requestedBusinessId, query: null, CatalogContentView.Services, ct);

        public Task<string> GenerateAsync(Guid requestedBusinessId, string? query, CancellationToken ct = default) =>
            GenerateAsync(requestedBusinessId, query, CatalogContentView.Services, ct);

        public Task<string> GenerateAsync(
            Guid requestedBusinessId,
            string? query,
            CatalogContentView view,
            CancellationToken ct = default)
        {
            requestedBusinessId.Should().Be(businessId);
            WasCalled = true;
            RequestedView = view;
            return Task.FromResult(content);
        }
    }
}
