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
        json.Should().Contain("\"view\":\"auto\"");
        catalog.WasCalled.Should().BeTrue();
        catalog.RequestedView.Should().Be(CatalogContentView.Auto);
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

    [Fact]
    public void Description_ExplainsAutoCatalogRouting()
    {
        var tool = new GetServiceCatalogTool(new FakeCatalogGenerator(Guid.NewGuid(), string.Empty));

        tool.Description.Should().Contain("Use view=auto when the turn should consult the catalog");
        tool.Description.Should().Contain("Use view=categories only when the customer should see the available service categories/options");
        tool.Description.Should().Contain("Pass query using the customer's own words");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAutoViewHasQuery_ForwardsAutoViewAndQuery()
    {
        var businessId = Guid.NewGuid();
        var catalog = new FakeCatalogGenerator(businessId, "## CATALOGO DE SERVICIOS\n- Corte infantil");
        var tool = new GetServiceCatalogTool(catalog);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("""{"view":"auto","query":"corte"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("Corte infantil");
        json.Should().Contain("\"view\":\"auto\"");
        json.Should().Contain("\"query\":\"corte\"");
        catalog.RequestedView.Should().Be(CatalogContentView.Auto);
        catalog.RequestedQuery.Should().Be("corte");
    }


    private sealed class FakeCatalogGenerator(Guid businessId, string content) : ICatalogContentGenerator
    {
        public bool WasCalled { get; private set; }
        public CatalogContentView? RequestedView { get; private set; }
        public string? RequestedQuery { get; private set; }

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
            RequestedQuery = query;
            return Task.FromResult(content);
        }
    }
}
