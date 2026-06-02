using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
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
        Mock.Of<IBookingPolicyProvider>(),
        Mock.Of<IPaymentLifecycleService>(),
        Mock.Of<IAvailabilityService>(),
        Mock.Of<ISchedulingPolicyProvider>(),
        Mock.Of<IConversationLifecycleService>());

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
        result.Remediation.Should().Contain("check_availability");
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
            VerificationFactTypes.CheckoutPrepared,
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
        result.Remediation.Should().Contain("prepare_checkout");
    }

    [Fact]
    public async Task EvaluateAsync_CheckAvailability_DuringAddonsOffering_IsBlocked()
    {
        var checkAvailabilityTool = new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>(),
            _verifications);

        var ctx = CreateContext();
        ctx.Config = CreateConfigWithAddonsStage();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts["baby_name"] = "Thomas";
        ctx.Facts["baby_age_months"] = "5";

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("stage_action_pending");
        result.Remediation.Should().Contain("set_fact");
        result.Remediation.Should().Contain("add_ons");
    }

    [Fact]
    public async Task EvaluateAsync_CheckAvailability_DuringDiscoveryWithMissingService_MentionsServiceFact()
    {
        var checkAvailabilityTool = new CheckAvailabilityTool(
            Mock.Of<IAvailabilityService>(),
            Mock.Of<ISchedulingPolicyProvider>(),
            Mock.Of<IEmployeeAssignmentService>(),
            Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>(),
            _verifications);

        var ctx = CreateContext();
        ctx.Config = CreateConfigWithAddonsStage();
        ctx.Facts["baby_name"] = "Thomas";
        ctx.Facts["baby_age_months"] = "5";

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-27"}""");
        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Remediation.Should().Contain("service");
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

    /// <summary>
    /// Config con guards declarativos equivalentes a lo que Mimi configura en producción.
    /// Los tests validan el comportamiento del GuardEvaluator con guards explícitos,
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
                    Hint = "Presenta catálogo y registra service con set_fact al elegir.",
                    AllowedTools = ["get_service_catalog", "set_fact"],
                    AdvanceWhenFacts = ["baby_name", "baby_age_months", "service"]
                },
                new AgentFlowStage
                {
                    Id = "addons_offering",
                    Goal = "Ofrecer complementos",
                    Hint = "Lista complementos y registra add_ons con set_fact.",
                    AllowedTools = ["get_service_catalog", "set_fact"],
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
            ["create_reservation"] = new()
            {
                Requires =
                [
                    "verification:availability_checked",
                    "verification:customer_identified",
                    "verification:checkout_prepared"
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
        BookingPolicy = new BookingPolicyParams(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
