using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class ToolCapabilityGateTests
{
    private readonly Mock<IConversationVerificationService> _verifications = new();
    private readonly ToolCapabilityGate _gate;
    private readonly CreateReservationTool _createReservationTool = new(
        Mock.Of<IReservationService>(),
        Mock.Of<IReservationIntentBuilder>(),
        Mock.Of<IBusinessRuleEngine>(),
        Mock.Of<IBookingPolicyProvider>(),
        Mock.Of<IPaymentLifecycleService>(),
        Mock.Of<IAvailabilityService>(),
        Mock.Of<ISchedulingPolicyProvider>());

    public ToolCapabilityGateTests()
    {
        _gate = new ToolCapabilityGate(new ToolPreconditionProvider(), _verifications.Object);
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithoutAvailabilityVerification_IsRejected()
    {
        _verifications
            .Setup(v => v.IsActiveAsync(
                It.IsAny<Guid>(),
                VerificationFactTypes.AvailabilityChecked,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _verifications
            .Setup(v => v.IsActiveAsync(
                It.IsAny<Guid>(),
                VerificationFactTypes.CustomerIdentified,
                SlotVerificationScope.UniversalScope,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("precondition_failed");
        result.Remediation.Should().Contain("check_availability");
    }

    [Fact]
    public async Task EvaluateAsync_CreateReservation_WithVerifications_IsAllowed()
    {
        _verifications
            .Setup(v => v.IsActiveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-22";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        using var args = JsonDocument.Parse("""{"customer_confirmed":true}""");
        var result = await _gate.EvaluateAsync(_createReservationTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_SetFact_HasNoPreconditions()
    {
        var setFactTool = new SetFactTool(
            Mock.Of<IConversationFactsService>(),
            Mock.Of<IAddOnCatalogService>(),
            _verifications.Object);

        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");

        var result = await _gate.EvaluateAsync(setFactTool, args.RootElement, ctx, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        _verifications.Verify(v => v.IsActiveAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        BusinessToday = new DateOnly(2026, 5, 21),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
