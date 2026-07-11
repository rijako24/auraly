using FluentAssertions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Runtime;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicStageTransitionResolverTests
{
    private readonly DeterministicStageTransitionResolver _resolver = new(new StageConditionEvaluator());

    [Fact]
    public void Resolve_UsesAdvanceWhenFactsAsDeterministicShorthand()
    {
        var stage = new AgentFlowStage { Id = "data", AdvanceWhenFacts = ["customer_name", "email"] };
        var flow = new AgentFlowDefinition
        {
            Id = "booking",
            Stages = [stage, new AgentFlowStage { Id = "checkout" }]
        };
        var context = Context(("customer_name", "Ana"), ("email", "ana@example.com"));

        var result = _resolver.Resolve(flow, stage, context);

        result.ShouldTransition.Should().BeTrue();
        result.TargetStageId.Should().Be("checkout");
        result.Reason.Should().Be("advance_facts_complete");
    }

    [Fact]
    public void Resolve_ExplicitConditionOverridesAdvanceShorthand()
    {
        var stage = new AgentFlowStage
        {
            Id = "availability",
            AdvanceWhenFacts = ["desired_date"],
            Transitions =
            [
                new StageTransitionDefinition
                {
                    Id = "available",
                    Priority = 10,
                    To = "checkout",
                    Condition = new StageConditionDefinition { VerificationActive = "availability" }
                },
                new StageTransitionDefinition
                {
                    Id = "needs_time",
                    Priority = 1,
                    To = "time_selection",
                    Condition = new StageConditionDefinition { VerificationMissing = "availability" }
                }
            ]
        };
        var flow = new AgentFlowDefinition
        {
            Id = "booking",
            Stages = [stage, new AgentFlowStage { Id = "checkout" }, new AgentFlowStage { Id = "time_selection" }]
        };

        var result = _resolver.Resolve(flow, stage, Context(("desired_date", "2026-07-11")));

        result.TargetStageId.Should().Be("time_selection");
        result.TransitionId.Should().Be("needs_time");
    }

    private static DeterministicStageExecutionContext Context(params (string Key, string Value)[] facts) => new()
    {
        Facts = facts.ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase)
    };
}
