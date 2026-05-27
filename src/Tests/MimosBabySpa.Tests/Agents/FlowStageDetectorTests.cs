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
    public void DetectCurrentStage_GreetingWithCompletesOnEnter_SkipsAfterOneShotMarked()
    {
        var flow = new AgentFlowDefinition
        {
            StageDetection = "automatic",
            Stages =
            [
                new AgentFlowStage { Id = "greeting", CompletesOnEnter = true, AdvanceWhenFacts = [] },
                new AgentFlowStage { Id = "discovery", AdvanceWhenFacts = ["service"] }
            ]
        };

        var state = new ConversationState();
        state.CompletedOneShotStages.Add("greeting");

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = state,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var stage = _detector.DetectCurrentStage(flow, session);

        stage!.Id.Should().Be("discovery");
    }

    [Fact]
    public void DetectCurrentStage_IntentCaptureCompletesOnEnter_ThenDiscovery()
    {
        var flow = new AgentFlowDefinition
        {
            StageDetection = "automatic",
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "intent_capture",
                    CompletesOnEnter = true,
                    AdvanceWhenFacts = []
                },
                new AgentFlowStage
                {
                    Id = "discovery",
                    AdvanceWhenFacts = ["service"]
                }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        _detector.DetectCurrentStage(flow, session)!.Id.Should().Be("intent_capture");

        session.ConversationState.CompletedOneShotStages.Add("intent_capture");
        _detector.DetectCurrentStage(flow, session)!.Id.Should().Be("discovery");
    }
}
