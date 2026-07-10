using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowRuntimeTests
{
    [Fact]
    public void Decide_LeavesGlobalActionsAvailableAndRuntimeNeutral()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);

        var decision = Decide(config, session);

        decision.State.Should().Be(FlowRuntimeState.Default);
        decision.DisabledToolCapabilities.Should().BeEmpty();
        decision.BlockedToolNames.Should().BeEmpty();
        decision.Route.ActiveFlowId.Should().Be("booking");
    }

    [Fact]
    public void Resolve_GlobalActionToolsBypassStageScopeWithoutReplacingStageTools()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        session.RuntimeDecision = Decide(config, session);

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout"), new StubTool("manage_reservation", [ToolCapabilities.ReservationManage]), new StubTool("get_customer_reservations", [ToolCapabilities.ReservationManage])],
            config.Flow.Stages[0]);

        scoped.Select(t => t.Name).Should().Contain("set_fact");
        scoped.Select(t => t.Name).Should().Contain("prepare_checkout");
        scoped.Select(t => t.Name).Should().Contain("manage_reservation");
        scoped.Select(t => t.Name).Should().Contain("get_customer_reservations");
    }

    [Fact]
    public async Task Gate_GlobalActionTool_BypassesStageBlock()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        session.RuntimeDecision = Decide(config, session);
        var gate = new ToolCapabilityGate(
            new GuardEvaluator(new ConversationVerificationService()),
            new FlowStageDetector());

        var result = await gate.EvaluateAsync(
            new StubTool("manage_reservation", [ToolCapabilities.ReservationManage]),
            JsonDocument.Parse("{}").RootElement,
            session,
            CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenManageableReservationExists_AllowsReservationManagementCapability()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        session.ManageableReservations =
        [
            new Reservation { Status = ReservationStatus.Confirmed }
        ];
        session.RuntimeDecision = Decide(config, session);

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
                Id = "booking",
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
        var session = CreateSession(config);

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout")],
            config.Flow.Stages[0]);

        scoped.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WhenConfiguredScopeReferencesUnknownToolName_ReturnsNoTools()
    {
        var config = new AgentConfig
        {
            Flow = new AgentFlowDefinition
            {
                Id = "booking",
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
        var session = CreateSession(config);

        var scoped = AgentTurnToolScope.Resolve(
            config,
            session,
            [new StubTool("set_fact"), new StubTool("prepare_checkout")],
            config.Flow.Stages[0]);

        scoped.Should().BeEmpty();
    }

    [Fact]
    public void BuildGlobalActionsBlock_RendersConfiguredGlobalActions()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        session.RuntimeDecision = Decide(config, session);

        var result = AgentPromptComposer.BuildGlobalActionsBlock(
            config,
            [new StubTool("escalate_to_human"), new StubTool("get_customer_reservations"), new StubTool("manage_reservation")],
            session);

        result.Should().Contain("tenant_human_handoff");
        result.Should().Contain("custom_action_id");
        result.Should().Contain("manage_reservation");
    }

    [Fact]
    public void StateResolver_IsNeutralAndDoesNotDependOnTenantStageIds()
    {
        var resolver = new FlowRuntimeStateResolver();
        var config = CreateConfig(globalActionId: "anything");
        var session = CreateSession(config);

        resolver.Resolve(config, session).Should().Be(FlowRuntimeState.Default);
    }

    [Fact]
    public async Task Router_ActivatesSecondaryFlow_WhenClassifierIsConfident()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        var chat = new StubChatClient("""{"flowId":"reservation_management","confidence":0.94,"reason":"existing reservation change"}""");
        var router = new FlowRouter(chat, new FlowStageDetector());

        var route = await router.RouteAsync(config, session, "quiero cambiar la hora de mi reserva", CancellationToken.None);

        route.ActiveFlowId.Should().Be("reservation_management");
        route.IsPrimaryFlow.Should().BeFalse();
        ActiveFlowRuntimeState.Get(session.ConversationState)?.FlowId.Should().Be("reservation_management");
    }

    [Fact]
    public async Task Router_UsesPrimaryFlow_WhenSecondaryConfidenceIsLow()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        var chat = new StubChatClient("""{"flowId":"reservation_management","confidence":0.50,"reason":"ambiguous"}""");
        var router = new FlowRouter(chat, new FlowStageDetector());

        var route = await router.RouteAsync(config, session, "a las 2", CancellationToken.None);

        route.ActiveFlowId.Should().Be("booking");
        route.IsPrimaryFlow.Should().BeTrue();
        ActiveFlowRuntimeState.Get(session.ConversationState).Should().BeNull();
    }

    [Fact]
    public async Task Router_ContinuesFreshSecondaryFlow_WhenAwaitedFactShapeMatches()
    {
        var config = CreateConfig(globalActionId: "custom_action_id");
        var session = CreateSession(config);
        ActiveFlowRuntimeState.Set(
            session.ConversationState,
            "reservation_management",
            DateTime.UtcNow,
            TimeSpan.FromMinutes(15),
            "start_secondary_flow",
            "existing reservation change");
        var chat = new StubChatClient("""{"flowId":"booking","confidence":1,"reason":"should not be called"}""");
        var router = new FlowRouter(chat, new FlowStageDetector());

        var route = await router.RouteAsync(config, session, "a las 2", CancellationToken.None);

        route.ActiveFlowId.Should().Be("reservation_management");
        route.Decision.Should().Be("continue_secondary_flow");
        chat.CallCount.Should().Be(0);
    }

    private static FlowRuntimeDecision Decide(AgentConfig config, AgentToolContext session) =>
        new FlowPolicyEngine().Decide(
            config,
            session,
            FlowRuntimeState.Default,
            [],
            FlowRouteDecision.Primary(AgentFlowCatalog.ResolvePrimaryFlowId(config)));

    private static AgentConfig CreateConfig(string globalActionId)
    {
        var bookingFlow = new AgentFlowDefinition
        {
            Id = "booking",
            Type = FlowTypes.Primary,
            RoutingGuidance = "Use this flow for new booking requests and general catalog questions.",
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "tenant_checkout_stage",
                    AllowedActions = ["set_fact", "prepare_checkout"]
                }
            ]
        };

        var reservationManagementFlow = new AgentFlowDefinition
        {
            Id = "reservation_management",
            Type = FlowTypes.Secondary,
            RoutingGuidance = "Use only when the customer clearly wants to view, cancel, confirm, or change an existing reservation.",
            TtlSeconds = 900,
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "reservation_change",
                    Collect = ["desired_time"],
                    AdvanceWhenFacts = ["desired_time"],
                    AllowedActions = ["get_customer_reservations", "manage_reservation", "escalate_to_human"]
                }
            ]
        };

        return new AgentConfig
        {
            Flow = bookingFlow,
            Flows = [bookingFlow, reservationManagementFlow],
            FactSchema =
            [
                new FactSchemaEntry { Key = "desired_time", Type = "time", Role = "booking.time" }
            ],
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "tenant_human_handoff",
                    AllowedActions = ["escalate_to_human"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "escalate_to_human",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches = [new StageEntryMessageMatch { AnyOf = ["hablar con una persona"] }]
                            }
                        }
                    ]
                },
                new AgentGlobalAction
                {
                    Id = globalActionId,
                    AllowedActions = ["get_customer_reservations", "manage_reservation"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "get_customer_reservations",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches = [new StageEntryMessageMatch { AnyOf = ["cambiar la hora de mi reserva", "reagendar mi reserva"] }]
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static AgentToolContext CreateSession(AgentConfig config) => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationState(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Config = config,
        LatestUserMessage = "quiero cambiar la hora de mi reserva y hablar con una persona"
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

    private sealed class StubChatClient : IChatClient
    {
        private readonly string _content;

        public StubChatClient(string content) => _content = content;

        public int CallCount { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ChatToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = _content,
                AssistantMessage = ChatMessage.Assistant(_content)
            });
        }
    }
}
