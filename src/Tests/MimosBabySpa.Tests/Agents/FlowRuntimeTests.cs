using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowRuntimeTests
{
    [Fact]
    public void Decide_WhenNoManageableReservation_DisablesConditionedGlobalAction()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession();

        var decision = new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            []);

        decision.EnabledGlobalActionIds.Should().NotContain("custom_action_id");
        decision.DisabledToolCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WhenNoManageableReservation_HidesGlobalActionToolsByCapability()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession();
        session.RuntimeDecision = new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            []);

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout"), new StubTool("manage_reservation", [ToolCapabilities.ReservationManage]), new StubTool("get_customer_reservations", [ToolCapabilities.ReservationManage])],
            config.Flow.Stages[0]);

        scoped.Select(t => t.Name).Should().Contain("prepare_checkout");
        scoped.Select(t => t.Name).Should().NotContain("manage_reservation");
        scoped.Select(t => t.Name).Should().NotContain("get_customer_reservations");
    }

    [Fact]
    public async Task Gate_WhenRuntimeConditionDisablesGlobalAction_ReturnsStageBlockWithoutHint()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession();
        session.Config = config;
        session.RuntimeDecision = new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            []);
        var gate = new ToolCapabilityGate(
            Mock.Of<IGuardEvaluator>(),
            new FlowStageDetector());

        var result = await gate.EvaluateAsync(
            new StubTool("manage_reservation", [ToolCapabilities.ReservationManage]),
            JsonDocument.Parse("{}").RootElement,
            session,
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
        result.Remediation.Should().BeNull();
    }

    [Fact]
    public void Resolve_WhenManageableReservationExists_AllowsReservationManagementCapability()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession();
        session.ManageableReservations =
        [
            new Reservation { Status = ReservationStatus.Confirmed }
        ];
        session.RuntimeDecision = new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            []);

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("manage_reservation", [ToolCapabilities.ReservationManage]), new StubTool("get_customer_reservations", [ToolCapabilities.ReservationManage])],
            config.Flow.Stages[0]);

        scoped.Select(t => t.Name).Should().Contain("manage_reservation");
        scoped.Select(t => t.Name).Should().Contain("get_customer_reservations");
    }

    [Fact]
    public void Resolve_WhenConfiguredScopeReferencesOnlyUnknownTools_ReturnsNoTools()
    {
        var config = new AgentConfig
        {
            Flow = new AgentFlowDefinition
            {
                Language = LanguageForTools("missing_tool"),

                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "tenant_stage",
                        AllowedActions = ["missing_tool"]
                    }
                ]
            }
        };
        var session = CreateSession();

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout")],
            config.Flow.Stages[0]);

        scoped.Should().BeEmpty();
    }
    [Fact]
    public void Resolve_WhenConfiguredScopeReferencesUnknownSemanticAction_ReturnsNoTools()
    {
        var config = new AgentConfig
        {
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "tenant_stage",
                        AllowedActions = ["unknown_action"]
                    }
                ]
            }
        };
        var session = CreateSession();

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout")],
            config.Flow.Stages[0]);

        scoped.Should().BeEmpty();
    }
    [Fact]
    public void BuildGlobalActionsBlock_WhenRuntimeConditionDisablesGlobalAction_DoesNotRenderIt()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession();
        session.RuntimeDecision = new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            []);

        var result = AgentPromptComposer.BuildGlobalActionsBlock(
            config,
            [new StubTool("escalate_to_human"), new StubTool("get_customer_reservations"), new StubTool("manage_reservation")],
            session);

        result.Should().Contain("tenant_human_handoff");
        result.Should().NotContain("custom_action_id");
        result.Should().NotContain("manage_reservation");
    }
    [Fact]
    public void StateResolver_IsNeutralAndDoesNotDependOnTenantStageIds()
    {
        var resolver = new FlowRuntimeStateResolver();
        var config = CreateConfig(globalActionId: "anything");
        var session = CreateSession();

        resolver.Resolve(config, session).Should().Be(FlowRuntimeState.Default);
    }

    private static AgentConfig CreateConfig(string globalActionId) => new()
    {
        Flow = new AgentFlowDefinition
        {
            Language = LanguageForTools("set_fact", "prepare_checkout", "escalate_to_human", "get_customer_reservations", "manage_reservation"),

            Stages =
            [
                new AgentFlowStage
                {
                    Id = "tenant_checkout_stage",
                    AllowedActions = ["set_fact", "prepare_checkout"]
                }
            ]
        },
        GlobalActions =
        [
            new AgentGlobalAction
            {
                Id = "tenant_human_handoff",
                AllowedActions = ["escalate_to_human"]
            },
            new AgentGlobalAction
            {
                Id = globalActionId,
                RuntimeWhenAny = ["context:ManageableReservations.any", "context:ActivePayment.Status=Confirmed&&context:ActivePayment.ReservationId=null"],
                AllowedActions = ["get_customer_reservations", "manage_reservation"]
            }
        ]
    };

    private static AgentToolContext CreateSession() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationState(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    private sealed class StubTool : IAgentTool
    {
        public StubTool(string name, IReadOnlyList<string>? capabilities = null)
        {
            Name = name;
            Capabilities = capabilities ?? [];
        }

        public string Name { get; }
        public IReadOnlyList<string> Capabilities { get; }
        public string Description => Name;
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(
            JsonElement arguments,
            AgentToolContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("""{"ok":true,"data":{}}""");
    }
    private static ConversationalFlowLanguage LanguageForTools(params string[] toolNames) => new()
    {
        Actions = toolNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                tool => tool,
                tool => new SemanticFlowAction
                {
                    Name = tool,
                    Purpose = $"Test action for {tool}.",
                    Tool = tool
                },
                StringComparer.OrdinalIgnoreCase)
    };

}
