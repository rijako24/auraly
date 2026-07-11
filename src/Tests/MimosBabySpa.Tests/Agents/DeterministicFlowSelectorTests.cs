using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Agents.Runtime;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicFlowSelectorTests
{
    private readonly DeterministicFlowSelector _selector = new();

    [Fact]
    public void Select_StartsSecondaryFlow_WhenValidatedPlanClearlyRequestsIt()
    {
        var result = _selector.Select(
            Config(),
            Plan("reservation_management", 0.94, "cambiar mi reserva"),
            new FlowSelectionContext(null, false));

        result.ActiveFlowId.Should().Be("reservation_management");
        result.Decision.Should().Be("start_secondary_flow");
        result.IsPrimaryFlow.Should().BeFalse();
    }

    [Fact]
    public void Select_ContinuesActiveSecondaryFlow_WhenPlanSelectsTheSameFlow()
    {
        var result = _selector.Select(
            Config(),
            Plan("reservation_management", 0.91, "mañana"),
            new FlowSelectionContext("reservation_management", false));

        result.ActiveFlowId.Should().Be("reservation_management");
        result.Decision.Should().Be("continue_secondary_flow");
    }

    [Fact]
    public void Select_KeepsPrimaryFlow_WhenPrimaryRequestIsOpen()
    {
        var result = _selector.Select(
            Config(),
            Plan("reservation_management", 0.99, "cambiar mi reserva"),
            new FlowSelectionContext(null, true));

        result.ActiveFlowId.Should().Be("booking");
        result.Reason.Should().Be("open_primary_request");
    }

    [Fact]
    public void Select_RejectsSecondaryFlowBelowThreshold()
    {
        var result = _selector.Select(
            Config(),
            Plan("reservation_management", 0.5, "cambiar mi reserva"),
            new FlowSelectionContext(null, false));

        result.ActiveFlowId.Should().Be("booking");
        result.Reason.Should().Be("secondary_below_activation_threshold");
    }

    private static TurnPlan Plan(string flow, double confidence, string? evidence) => new()
    {
        FlowIntent = new PlannedFlowIntent
        {
            CandidateFlow = flow,
            Confidence = confidence,
            Evidence = evidence
        }
    };

    private static AgentConfig Config() => new()
    {
        Flows =
        [
            new AgentFlowDefinition { Id = "booking", Type = FlowTypes.Primary },
            new AgentFlowDefinition { Id = "reservation_management", Type = FlowTypes.Secondary }
        ]
    };
}
