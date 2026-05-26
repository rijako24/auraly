using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowLookupGateTests
{
    [Fact]
    public void CanExecute_returns_false_when_required_date_fact_missing()
    {
        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            Lookup = new AgentFlowStageLookup
            {
                Tool = "check_availability",
                Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["service"] = "@fact.service",
                    ["date"] = "@fact.desired_date"
                }
            }
        };

        var session = AgentTestHelpers.CreateSession(AgentTestHelpers.MinimalConfig());
        session.Facts["service"] = "Plan Marineritos";

        FlowLookupGate.CanExecute(stage, session, new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<IUnitOfWork>(),
            new ConversationVerificationService())).Should().BeFalse();
    }

    [Fact]
    public void CanExecute_returns_true_when_required_facts_present()
    {
        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            Lookup = new AgentFlowStageLookup
            {
                Tool = "check_availability",
                Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["service"] = "@fact.service",
                    ["date"] = "@fact.desired_date"
                }
            }
        };

        var session = AgentTestHelpers.CreateSession(AgentTestHelpers.MinimalConfig());
        session.Facts["service"] = "Plan Marineritos";
        session.Facts["desired_date"] = "2026-05-27";

        FlowLookupGate.CanExecute(stage, session, new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<IUnitOfWork>(),
            new ConversationVerificationService())).Should().BeTrue();
    }
}
