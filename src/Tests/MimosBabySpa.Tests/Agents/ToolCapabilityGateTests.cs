using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
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
        Mock.Of<IReservationService>(),
        Mock.Of<IReservationIntentBuilder>(),
        Mock.Of<IBusinessRuleEngine>(),
        Mock.Of<IAvailabilityService>(),
        Mock.Of<ISchedulingPolicyProvider>(),
        CreateServiceNameResolver(),
        NullLogger<CreateReservationTool>.Instance);

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
    public async Task EvaluateAsync_CreateReservation_WithPendingCheckout_IsRejectedByDeclarativeGuard()
    {
        var ctx = CreateContext();
        ctx.ActivePayment = new PaymentTransaction { Status = PaymentTransactionStatus.Created };
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
        result.Reason.Should().Contain("pending checkout");
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithoutCheckoutPrepared_IsRejected()
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
        result.Remediation.Should().Contain("Lista complementos");
        result.Remediation.Should().NotContain("datos faltantes");
        result.Remediation.Should().NotContain("Antes registra");
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
        result.Remediation.Should().Contain("Presenta catalogo");
        result.Remediation.Should().NotContain("datos faltantes");
        result.Remediation.Should().NotContain("Antes registra");
    }

    [Fact]
    public async Task EvaluateAsync_SetFact_HasNoPreconditions()
    {
        var services = new Mock<IServiceRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(services.Object);
        var resolver = new ServiceSelectionResolver(unitOfWork.Object, NullLogger<ServiceSelectionResolver>.Instance);
        var setFactTool = new SetFactTool(
            Mock.Of<IConversationFactsService>(),
            resolver,
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
        var suspendTool = new SuspendReservationTool(
            Mock.Of<IReservationService>(),
            Mock.Of<ICustomerReservationResolver>());

        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            EnabledToolNames = ["set_fact", "suspend_reservation"],
            Flow = new AgentFlowDefinition
            {
                Language = LanguageForTools("set_fact", "suspend_reservation"),

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
                    AllowedActions = ["suspend_reservation"]
                }
            ]
        };

        using var args = JsonDocument.Parse("{}");
        var result = await _gate.EvaluateAsync(suspendTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
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
                Language = LanguageForTools("search_products", "add_order_item", "reset_flow_context"),

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
                    AllowedActions = ["reset_flow_context"]
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

        var visibleTools = ToolFlowScope.FilterVisibleTools(config, currentStage, effectiveTools);

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
            EnabledToolNames = ["set_fact", "check_availability", "suspend_reservation"],
            Flow = new AgentFlowDefinition
            {
                Language = LanguageForTools("set_fact", "suspend_reservation"),

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
                    AllowedActions = ["suspend_reservation"]
                }
            ]
        };

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
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
            Language = LanguageForTools("get_service_catalog", "set_fact"),

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
        Guards = new Dictionary<string, MimosBabySpa.Application.Agents.Configuration.GuardDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["capability:reservation.create"] = new()
            {
                Requires =
                [
                    "verification:availability_checked",
                    "verification:customer_identified",
                    "verification:checkout_no_payment_prepared",
                    "state:no_pending_checkout"
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
