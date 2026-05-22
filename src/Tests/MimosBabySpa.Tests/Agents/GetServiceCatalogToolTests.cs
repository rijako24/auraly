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
        var catalog = new FakeCatalogGenerator(businessId, "## CATÁLOGO DE SERVICIOS\n- Plan Test");
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
        catalog.WasCalled.Should().BeTrue();
    }

    private sealed class FakeCatalogGenerator(Guid businessId, string content) : ICatalogContentGenerator
    {
        public bool WasCalled { get; private set; }

        public Task<string> GenerateAsync(Guid requestedBusinessId, CancellationToken ct = default)
        {
            requestedBusinessId.Should().Be(businessId);
            WasCalled = true;
            return Task.FromResult(content);
        }
    }
}
