using System.Text.Json;

using FluentAssertions;

using Moq;

using MimosBabySpa.Application.Agents;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Identity;

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

        Mock.Of<IPaymentLifecycleService>(),

        Mock.Of<IAvailabilityService>(),

        Mock.Of<ISchedulingPolicyProvider>(),

        Mock.Of<IConversationLifecycleService>());



    public ToolCapabilityGateTests()

    {

        _gate = new ToolCapabilityGate(new GuardEvaluator(_verifications));

    }



    [Fact]

    public async Task EvaluateAsync_CreateReservation_WithoutAvailabilityVerification_IsRejected()

    {

        var ctx = CreateContext();

        ctx.Facts["service"] = "Plan Marineritos";

        ctx.Facts["desired_date"] = "2026-05-22";

        ctx.Facts["desired_time"] = "09:00";



        _verifications.Record(

            ctx,

            VerificationFactTypes.CustomerIdentified,

            SlotVerificationScope.UniversalScope,

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

        ctx.Facts["service"] = "Plan Marineritos";

        ctx.Facts["desired_date"] = "2026-05-22";

        ctx.Facts["desired_time"] = "09:00";



        _verifications.Record(

            ctx,

            VerificationFactTypes.AvailabilityChecked,

            SlotVerificationScope.Build("Plan Marineritos", "2026-05-22", "09:00"),

            VerificationTtl.AvailabilityChecked);



        _verifications.Record(

            ctx,

            VerificationFactTypes.CustomerIdentified,

            SlotVerificationScope.UniversalScope,

            ttl: null);



        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");

        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeTrue();

    }



    [Fact]

    public async Task EvaluateAsync_SetFact_HasNoPreconditions()

    {

        var setFactTool = new SetFactTool(

            Mock.Of<IConversationFactsService>(),

            new MimosBabySpa.Application.Agents.Facts.FactAccessor(),

            Mock.Of<IAddOnCatalogService>(),

            _verifications,

            Mock.Of<IIdentityAttributeService>(),

            Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>());



        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");



        var result = await _gate.EvaluateAsync(setFactTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeTrue();

    }



    [Fact]

    public async Task EvaluateAsync_CheckAvailability_WithoutServiceFact_IsRejected()

    {

        var checkAvailabilityTool = CreateCheckAvailabilityTool();

        var ctx = CreateContext();

        ctx.Facts["desired_date"] = "2026-05-23";



        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-23"}""");

        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeFalse();

        result.Code.Should().Be("precondition_failed");

        result.Reason.Should().Contain("Missing required fact 'service'");

    }



    [Fact]

    public async Task EvaluateAsync_CheckAvailability_WithRequiredFacts_IsAllowed()

    {

        var checkAvailabilityTool = CreateCheckAvailabilityTool();

        var ctx = CreateContext();

        ctx.Facts["service"] = "Plan Marineritos";

        ctx.Facts["desired_date"] = "2026-05-23";



        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-23","time":"08:00"}""");

        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeTrue();

    }



    [Fact]

    public async Task EvaluateAsync_CheckAvailability_AllowedAfterSchedulingFactsComplete()

    {

        var checkAvailabilityTool = CreateCheckAvailabilityTool();

        var ctx = CreateContext();

        ctx.Facts["service"] = "Plan Marineritos";

        ctx.Facts["add_ons"] = "ninguno";

        ctx.Facts["desired_date"] = "2026-05-23";

        ctx.Facts["desired_time"] = "08:00";



        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos","date":"2026-05-23","time":"08:00"}""");

        var result = await _gate.EvaluateAsync(checkAvailabilityTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeTrue();

    }



    [Fact]

    public async Task EvaluateAsync_PrepareCheckout_WithoutAddOns_IsRejected()

    {

        var prepareCheckoutTool = new PrepareCheckoutTool(

            Mock.Of<IReservationCheckoutPricing>(),

            Mock.Of<IAddOnCatalogService>(),

            Mock.Of<IPaymentLinkService>(),

            Mock.Of<IPaymentLifecycleService>(),

            Mock.Of<IReservationIntentBuilder>(),

            Mock.Of<IAvailabilityService>(),

            Mock.Of<ISchedulingPolicyProvider>(),

            Mock.Of<IEmployeeAssignmentService>());



        var ctx = CreateContext();

        ctx.Facts["service"] = "Plan Marineritos";

        ctx.Facts["desired_date"] = "2026-05-23";

        ctx.Facts["desired_time"] = "08:00";

        ctx.Facts["customer_name"] = "Ana";



        _verifications.Record(

            ctx,

            VerificationFactTypes.AvailabilityChecked,

            SlotVerificationScope.Build("Plan Marineritos", "2026-05-23", "08:00"),

            VerificationTtl.AvailabilityChecked);



        using var args = JsonDocument.Parse("{}");

        var result = await _gate.EvaluateAsync(prepareCheckoutTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeFalse();

        result.Reason.Should().Contain("Missing required fact 'add_ons'");

    }



    [Fact]

    public async Task EvaluateAsync_EscalateToHuman_AllowedWithoutGuards()

    {

        var escalateTool = new EscalateToHumanTool(Mock.Of<IEscalationNotifier>());

        var ctx = CreateContext();



        using var args = JsonDocument.Parse("""{"reason":"explicit_human_request"}""");

        var result = await _gate.EvaluateAsync(escalateTool, args.RootElement, ctx, CancellationToken.None);



        result.IsAllowed.Should().BeTrue();

    }



    private static CheckAvailabilityTool CreateCheckAvailabilityTool() => new(

        Mock.Of<IAvailabilityService>(),

        Mock.Of<ISchedulingPolicyProvider>(),

        Mock.Of<IEmployeeAssignmentService>(),

        Mock.Of<MimosBabySpa.Domain.Repositories.IUnitOfWork>(),

        new ConversationVerificationService());



    private static AgentConfig CreateConfigWithGuards() => new()

    {

        AgentId = Guid.NewGuid(),

        BusinessId = Guid.NewGuid(),

        EnabledToolNames =

        [

            "create_reservation",

            "check_availability",

            "prepare_checkout",

            "escalate_to_human"

        ],

        FactSchema = AgentTestHelpers.MimiFactSchema,

        Guards = new Dictionary<string, MimosBabySpa.Application.Agents.Configuration.GuardDefinition>(StringComparer.OrdinalIgnoreCase)

        {

            ["check_availability"] = new()

            {

                Requires = ["fact:service", "fact:desired_date"]

            },

            ["prepare_checkout"] = new()

            {

                Requires =

                [

                    "fact:service",

                    "fact:desired_date",

                    "fact:desired_time",

                    "fact:customer_name",

                    "fact:add_ons",

                    "verification:availability_checked"

                ]

            },

            ["create_reservation"] = new()

            {

                Requires = ["verification:availability_checked", "verification:customer_identified"]

            }

        }

    };



    private static AgentToolContext CreateContext()

    {

        var ctx = new AgentToolContext

        {

            BusinessId = Guid.NewGuid(),

            ConversationId = Guid.NewGuid(),

            BusinessToday = new DateOnly(2026, 5, 21),

            Config = CreateConfigWithGuards(),

            ConversationState = new ConversationStateModel(),

            Conversation = new Conversation(),

            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        };



        AgentTestHelpers.SetBookingPack(ctx, new BookingPolicyParams());

        return ctx;

    }

}


