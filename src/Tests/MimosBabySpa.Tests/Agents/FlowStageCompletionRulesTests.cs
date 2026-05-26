using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class FlowStageCompletionRulesTests
{
    [Fact]
    public void ApplyEndOfTurn_returns_true_when_facts_complete_during_turn()
    {
        var stage = new AgentFlowStage
        {
            Id = "discovery",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["baby_name", "baby_age_months"]
        };

        var session = BuildSession(facts: new() { ["baby_name"] = "Thomas" });
        var atTurnStart = FlowStageCompletionRules.SnapshotCompletedOneShotStages(session);

        session.Facts["baby_age_months"] = "5";

        var justCompleted = FlowStageCompletionRules.ApplyEndOfTurn(
            session, stage, atTurnStart, new FlowTurnResult());

        justCompleted.Should().BeTrue();
        session.ConversationState.CompletedOneShotStages.Should().Contain("discovery");
    }

    [Fact]
    public void ApplyEndOfTurn_completes_stage_when_result_collect_satisfied_by_lookup()
    {
        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"]
        };

        var session = BuildSession(facts: new()
        {
            ["desired_date"] = "2026-05-26",
            ["desired_time"] = "10:00"
        });
        var atTurnStart = FlowStageCompletionRules.SnapshotCompletedOneShotStages(session);
        var lookupResult = FlowToolResult.Parse(
            """{"ok":true,"data":{"slot_confirmed":true,"time":"10:00","available_slots":[]}}""");

        var justCompleted = FlowStageCompletionRules.ApplyEndOfTurn(
            session, stage, atTurnStart, new FlowTurnResult(), lookupResult);

        justCompleted.Should().BeTrue();
        session.ConversationState.CompletedOneShotStages.Should().Contain("scheduling");
    }

    [Fact]
    public void ApplyEndOfTurn_returns_false_when_stage_already_marked_at_turn_start()
    {
        var stage = new AgentFlowStage
        {
            Id = "discovery",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["baby_name", "baby_age_months"]
        };

        var state = BuildConversationState(completedOneShot: ["discovery"]);
        var session = BuildSession(
            state: state,
            facts: new()
            {
                ["baby_name"] = "Thomas",
                ["baby_age_months"] = "5"
            });

        var atTurnStart = FlowStageCompletionRules.SnapshotCompletedOneShotStages(session);

        var justCompleted = FlowStageCompletionRules.ApplyEndOfTurn(
            session, stage, atTurnStart, new FlowTurnResult());

        justCompleted.Should().BeFalse();
    }

    [Fact]
    public void IsStageCompleted_factsCollected_true_when_marked_in_one_shot_without_facts()
    {
        var stage = new AgentFlowStage
        {
            Id = "discovery",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["baby_name", "baby_age_months"]
        };

        var state = BuildConversationState(completedOneShot: ["discovery"]);
        var session = BuildSession(state: state, facts: new());

        FlowStageCompletionRules.IsStageCompleted(stage, session).Should().BeTrue();
    }

    [Fact]
    public void FlowStageDetector_skips_discovery_when_marked_in_one_shot_without_facts()
    {
        var flow = BuildFlow(
            ("greeting", StageCompletionCriteria.Always, []),
            ("discovery", StageCompletionCriteria.FactsCollected, ["baby_name", "baby_age_months"]),
            ("service_presentation", StageCompletionCriteria.FactsCollected, ["service"]));

        var state = BuildConversationState(completedOneShot: ["greeting", "discovery"]);
        var session = BuildSession(state: state, facts: new());

        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, session);

        stage.Should().NotBeNull();
        stage!.Id.Should().Be("service_presentation");
    }

    private static AgentToolContext BuildSession(
        ConversationState? state = null,
        Dictionary<string, string>? facts = null) =>
        new()
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ConversationState = state ?? BuildConversationState(),
            Conversation = new Conversation(),
            Facts = facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    private static ConversationState BuildConversationState(
        IEnumerable<string>? completedOneShot = null) =>
        new()
        {
            CompletedOneShotStages = new HashSet<string>(
                completedOneShot ?? [], StringComparer.OrdinalIgnoreCase)
        };

    private static AgentFlowDefinition BuildFlow(
        params (string Id, string CompletedWhen, string[] Collects)[] stages) =>
        new()
        {
            Stages = stages.Select(s => new AgentFlowStage
            {
                Id = s.Id,
                CompletedWhen = s.CompletedWhen,
                Collects = s.Collects
            }).ToList()
        };
}
