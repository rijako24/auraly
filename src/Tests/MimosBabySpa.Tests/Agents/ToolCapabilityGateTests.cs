using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations.Reservation;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class ToolCapabilityGateTests
{
    private readonly ConversationVerificationService _verifications = new();
    private readonly ToolCapabilityGate _gate;
    private readonly CreateReservationTool _createReservationTool = new(
        Mock.Of<IReservationCreationService>());

    private static ServiceNameResolver CreateServiceNameResolver()
    {
        var services = new Mock<IServiceRepository>();
        services.Setup(r => r.GetActiveByBusinessIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<Service>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(services.Object);
        return new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);
    }

    public ToolCapabilityGateTests()
    {
        _gate = new ToolCapabilityGate(
            new GuardEvaluator(_verifications),
            new FlowStageDetector());
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithoutAvailabilityVerification_IsRejected()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";
        ctx.Facts["customer_confirmed"] = "true";
        ctx.LatestUserMessage = "confirmo";

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(),
            ttl: null);

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("precondition_failed");
        result.Remediation.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithVerifications_IsAllowed()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";
        ctx.Facts["customer_confirmed"] = "true";
        ctx.LatestUserMessage = "confirmo";

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(ConversationFactKeys.Service, "Plan Marineritos"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredDate, "2026-05-22"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredTime, "09:00")),
            VerificationTtl.AvailabilityChecked);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(),
            ttl: null);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutNoPaymentPrepared,
            VerificationSnapshot.Of(ctx.Facts,
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime,
                ConversationFactKeys.AddOns),
            ttl: null);

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithoutVerbalConfirmationFact_IsRejected()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(ConversationFactKeys.Service, "Plan Marineritos"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredDate, "2026-05-22"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredTime, "09:00")),
            VerificationTtl.AvailabilityChecked);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(),
            ttl: null);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutNoPaymentPrepared,
            VerificationSnapshot.Of(ctx.Facts,
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime,
                ConversationFactKeys.AddOns),
            ttl: null);

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("precondition_failed");
        result.Reason.Should().Contain("verbal confirmation");
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithPendingCheckout_IsRejectedByDeclarativeGuard()
    {
        var ctx = CreateContext();
        ctx.ActivePayment = new PaymentTransaction { Status = PaymentTransactionStatus.Created };
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";
        ctx.Facts["customer_confirmed"] = "true";
        ctx.LatestUserMessage = "confirmo";

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(ConversationFactKeys.Service, "Plan Marineritos"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredDate, "2026-05-22"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredTime, "09:00")),
            VerificationTtl.AvailabilityChecked);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(),
            ttl: null);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutNoPaymentPrepared,
            VerificationSnapshot.Of(ctx.Facts,
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime,
                ConversationFactKeys.AddOns),
            ttl: null);

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("precondition_failed");
        result.Reason.Should().Contain("pending checkout");
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithoutCheckoutPrepared_IsRejected()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";
        ctx.Facts["customer_confirmed"] = "true";
        ctx.LatestUserMessage = "confirmo";

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(ConversationFactKeys.Service, "Plan Marineritos"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredDate, "2026-05-22"),
                new KeyValuePair<string, string>(ConversationFactKeys.DesiredTime, "09:00")),
            VerificationTtl.AvailabilityChecked);

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(),
            ttl: null);

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("precondition_failed");
        result.Remediation.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_CheckAvailability_DuringAddonsOffering_IsBlocked()
    {
        var checkAvailabilityTool = new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>(),
            _verifications,
            null!);

        var ctx = CreateContext();
        ctx.Config = CreateConfigWithAddonsStage();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts["baby_name"] = "Thomas";
        ctx.Facts["baby_age_months"] = "5";

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
        result.Reason.Should().Contain("addons_offering");
        result.Remediation.Should().BeNull();
        AssertStagePendingLlm(result, "addons_offering", "add_ons");
    }

    [Fact]
    public async Task EvaluateAsync_CheckAvailability_DuringDiscoveryWithMissingService_MentionsServiceFact()
    {
        var checkAvailabilityTool = new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>(),
            _verifications,
            null!);

        var ctx = CreateContext();
        ctx.Config = CreateConfigWithAddonsStage();
        ctx.Facts["baby_name"] = "Thomas";
        ctx.Facts["baby_age_months"] = "5";

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
        result.Reason.Should().Contain("discovery");
        result.Remediation.Should().BeNull();
        AssertStagePendingLlm(result, "discovery", "service");
    }

    [Fact]
    public async Task EvaluateAsync_SetFact_HasNoPreconditions()
    {
        var setFactTool = new SetFactTool(
            Mock.Of<IConversationFactsService>(),
            Mock.Of<IAddOnCatalogService>(),
            _verifications,
            Mock.Of<ILeadService>());

        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");

        var result = await _gate.EvaluateAsync(setFactTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GlobalActionTool_BypassesStageWhitelist()
    {
        var manageTool = new TestTool("manage_reservation");

        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            EnabledToolNames = ["set_fact", "manage_reservation"],
            Flow = new AgentFlowDefinition
            {

                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "customer_data",
                        Goal = "Pedir datos",
                        ConversationGuidance = "Pide datos del cliente.",
                        AllowedActions = ["set_fact"]
                    }
                ]
            },
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "manage_existing_reservation",
                    AllowedActions = ["manage_reservation"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "manage_reservation",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches = [new StageEntryMessageMatch { AnyOf = ["cambiar la hora de mi reserva"] }]
                            }
                        }
                    ]
                }
            ]
        };

        ctx.LatestUserMessage = "quiero cambiar la hora de mi reserva";
        using var args = JsonDocument.Parse("{}");
        var result = await _gate.EvaluateAsync(manageTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GlobalActionWithEntryAction_RequiresMatchingMessage()
    {
        var manageTool = new TestTool("manage_reservation");

        var ctx = CreateContext();
        ctx.LatestUserMessage = "quiero cambiar la hora";
        ctx.Config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            EnabledToolNames = ["set_fact", "manage_reservation"],
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "customer_data",
                        Goal = "Pedir datos",
                        ConversationGuidance = "Pide datos del cliente.",
                        AllowedActions = ["set_fact"]
                    }
                ]
            },
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "manage_existing_reservation",
                    AllowedActions = ["manage_reservation"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "manage_reservation",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches =
                                [
                                    new StageEntryMessageMatch
                                    {
                                        AnyOf = ["cambiar la hora de mi reserva"]
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };

        using var args = JsonDocument.Parse("{}");
        var blocked = await _gate.EvaluateAsync(manageTool, args.RootElement, ctx, CancellationToken.None);
        blocked.IsAllowed.Should().BeFalse();
        blocked.Code.Should().Be("stage_action_pending");

        ctx.LatestUserMessage = "quiero cambiar la hora de mi reserva";
        var allowed = await _gate.EvaluateAsync(manageTool, args.RootElement, ctx, CancellationToken.None);
        allowed.IsAllowed.Should().BeTrue();
    }
    [Fact]
    public void FilterVisibleTools_StageWhitelist_ExposesOnlyStageAndGlobalActionTools()
    {
        var config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Flow = new AgentFlowDefinition
            {

                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Construir carrito",
                        AllowedActions = ["search_products", "add_order_item"]
                    }
                ]
            },
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "restart_order",
                    AllowedActions = ["reset_flow_context"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "reset_flow_context",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches = [new StageEntryMessageMatch { AnyOf = ["reiniciar pedido"] }]
                            }
                        }
                    ]
                }
            ]
        };

        var currentStage = config.Flow.Stages[0];
        IAgentTool[] effectiveTools =
        [
            new TestTool("search_products"),
            new TestTool("add_order_item"),
            new TestTool("create_order"),
            new TestTool("send_message_sequence"),
            new TestTool("reset_flow_context")
        ];

        var visibleTools = ToolFlowScope.FilterVisibleTools(config, currentStage, effectiveTools, new AgentToolContext
        {
            ConversationState = new ConversationStateModel(),
            LatestUserMessage = "quiero reiniciar pedido"
        });

        visibleTools.Select(t => t.Name).Should().Equal(
            "search_products",
            "add_order_item",
            "reset_flow_context");
    }


    [Fact]
    public async Task EvaluateAsync_NonGlobalTool_StillRespectsStageWhitelist()
    {
        var checkAvailabilityTool = new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<IUnitOfWork>(),
            _verifications,
            null!);

        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            EnabledToolNames = ["set_fact", "check_availability", "manage_reservation"],
            Flow = new AgentFlowDefinition
            {

                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "customer_data",
                        Goal = "Pedir datos",
                        ConversationGuidance = "Pide datos del cliente.",
                        AllowedActions = ["set_fact"]
                    }
                ]
            },
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "manage_existing_reservation",
                    AllowedActions = ["manage_reservation"]
                }
            ]
        };

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
    }


    private static void AssertStagePendingLlm(GateResult result, string stageId, params string[] expectedMissingFacts)
    {
        result.Llm.Should().NotBeNull();

        var json = JsonSerializer.Serialize(result.Llm);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("next_action").GetString().Should().Be("continue_current_stage");
        root.GetProperty("stage_id").GetString().Should().Be(stageId);

        var missingFacts = root.GetProperty("missing_facts")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        missingFacts.Should().Contain(expectedMissingFacts);
    }

    private sealed class TestTool : IAgentTool
    {
        public TestTool(string name) => Name = name;

        public string Name { get; }
        public string Description => Name;
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(
            JsonElement arguments,
            AgentToolContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("{\"ok\":true}");
    }


    /// <summary>
    /// Config con guards declarativos equivalentes a lo que Mimi configura en produccion.
    /// Los tests validan el comportamiento del GuardEvaluator con guards explicitos,
    /// no con precondiciones hardcoded (ToolPreconditionProvider eliminado).
    /// </summary>
    private static AgentConfig CreateConfigWithAddonsStage() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        EnabledToolNames = ["set_fact", "get_service_catalog", "check_availability"],
        Flow = new AgentFlowDefinition
        {

            StageDetection = "automatic",
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "discovery",
                    Goal = "discovery",
                    ConversationGuidance = "Presenta catalogo y registra service con set_fact al elegir.",
                    AllowedActions = ["get_service_catalog", "set_fact"],
                    AdvanceWhenFacts = ["baby_name", "baby_age_months", "service"]
                },
                new AgentFlowStage
                {
                    Id = "addons_offering",
                    Goal = "Ofrecer complementos",
                    ConversationGuidance = "Lista complementos y registra add_ons con set_fact.",
                    AllowedActions = ["get_service_catalog", "set_fact"],
                    AdvanceWhenFacts = ["add_ons"]
                }
            ]
        }
    };

    private static AgentConfig CreateConfigWithGuards() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        EnabledToolNames = ["create_reservation"],
        FactSchema =
        [
            new FactSchemaEntry
            {
                Key = "customer_confirmed",
                Role = "confirmation.verbal",
                Type = "boolean",
                Source = "user",
                Aliases = ["confirmo"]
            }
        ],
        Guards = new Dictionary<string, MimosBabySpa.Application.Agents.Configuration.GuardDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["capability:reservation.create"] = new()
            {
                Requires =
                [
                    "verification:availability_checked",
                    "verification:customer_identified",
                    "verification:checkout_no_payment_prepared",
                    "state:no_pending_checkout",
                    "flag:verbal_confirmation"
                ]
            }
        }
    };

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        BusinessToday = new DateOnly(2026, 5, 21),
        Config = CreateConfigWithGuards(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
