using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class LeadQualificationResolverTests
{
    [Fact]
    public void Resolve_MapsConfiguredStageWithoutChangingConversionBeforeCompletion()
    {
        var config = Config();

        var result = LeadQualificationResolver.Resolve(config, "journey", "visit", requestCompleted: false);

        result.Should().BeEquivalentTo(new LeadQualificationSnapshot(
            "high_intent", 80, "Quiere visita", "journey", "visit", false));
    }

    [Fact]
    public void Resolve_MarksConversionOnlyWhenConfiguredStageCompletesRequest()
    {
        var config = Config();

        var beforeCompletion = LeadQualificationResolver.Resolve(config, "journey", "handoff", requestCompleted: false);
        var afterCompletion = LeadQualificationResolver.Resolve(config, "journey", "handoff", requestCompleted: true);

        beforeCompletion!.Converted.Should().BeFalse();
        afterCompletion!.Converted.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ReturnsNullWhenQualificationIsNotConfigured()
    {
        LeadQualificationResolver.Resolve(Config(), "journey", "unqualified", false).Should().BeNull();
    }

    private static AgentConfig Config() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        Flows =
        [
            new AgentFlowDefinition
            {
                Id = "journey",
                Type = FlowTypes.Primary,
                Stages =
                [
                    new AgentFlowStage { Id = "unqualified", Goal = "Neutral" },
                    new AgentFlowStage { Id = "visit", Goal = "Visit", LeadQualification = new() { Band = "high_intent", Priority = 80, Label = "Quiere visita" } },
                    new AgentFlowStage { Id = "handoff", Goal = "Handoff", LeadQualification = new() { Band = "sales_ready", Priority = 100, ConversionOnRequestCompleted = true } }
                ]
            }
        ]
    };
}
