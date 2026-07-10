using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentFlowCatalogTests
{
    [Fact]
    public void ResolvePrimaryFlowId_UsesConfiguredPrimaryFlow()
    {
        var config = new AgentConfig
        {
            Flows =
            [
                new AgentFlowDefinition { Id = "reservation_management", Type = FlowTypes.Secondary },
                new AgentFlowDefinition { Id = "booking", Type = FlowTypes.Primary }
            ]
        };

        AgentFlowCatalog.ResolvePrimaryFlowId(config).Should().Be("booking");
    }

    [Fact]
    public void ResolvePrimaryFlowId_DoesNotInventFallbackFlowId()
    {
        var config = new AgentConfig();

        AgentFlowCatalog.ResolvePrimaryFlowId(config).Should().BeEmpty();
    }
}
