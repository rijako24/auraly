using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowStageDetectorTests
{
    private readonly FlowStageDetector _detector = new();

    [Fact]
    public void DetectCurrentStage_WhenAllPriorFactsPresent_ReturnsTerminalStage()
    {
        var flow = new AgentFlowDefinition
        {
            StageDetection = "automatic",
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "scheduling",
                    AdvanceWhenFacts = ["desired_date", "desired_time"]
                },
                new AgentFlowStage { Id = "finalization", AdvanceWhenFacts = [] }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["desired_date"] = "2026-05-27",
                ["desired_time"] = "08:00"
            }
        };

        var stage = _detector.DetectCurrentStage(flow, session);

        stage.Should().NotBeNull();
        stage!.Id.Should().Be("finalization");
    }

    [Fact]
    public void DetectCurrentStage_WhenFirstBusinessStageHasMissingFacts_ReturnsIt()
    {
        var flow = new AgentFlowDefinition
        {
            StageDetection = "automatic",
            Stages =
            [
                new AgentFlowStage { Id = "discovery", AdvanceWhenFacts = ["service"] },
                new AgentFlowStage { Id = "finalization", AdvanceWhenFacts = [] }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var stage = _detector.DetectCurrentStage(flow, session);

        stage!.Id.Should().Be("discovery");
    }
}
