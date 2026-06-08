using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class CreateReservationToolTests
{
    private readonly Mock<IReservationService> _reservations = new();
    private readonly Mock<IReservationIntentBuilder> _intentBuilder = new();
    private readonly Mock<IBusinessRuleEngine> _rules = new();
    private readonly Mock<IPaymentLifecycleService> _paymentLifecycle = new();
    private readonly Mock<IAvailabilityService> _availability = new();
    private readonly Mock<ISchedulingPolicyProvider> _schedulingPolicy = new();
    private readonly Mock<IConversationLifecycleService> _lifecycle = new();
    private readonly CreateReservationTool _tool;

    public CreateReservationToolTests()
    {
        _rules.Setup(r => r.ValidateReservationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });

        _schedulingPolicy.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailabilityParams.Default);

        _availability.Setup(a => a.CheckAvailabilityAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<AvailabilityParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult { IsAvailable = true });

        _tool = new CreateReservationTool(
            _reservations.Object,
            _intentBuilder.Object,
            _rules.Object,
            _paymentLifecycle.Object,
            _availability.Object,
            _schedulingPolicy.Object,
            _lifecycle.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPaymentPending_ReturnsPaymentPending()
    {
        _paymentLifecycle.Setup(p => p.GetActiveByConversationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentTransaction { Status = PaymentTransactionStatus.Created });

        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""
            {
              "service":"Plan Marineritos",
              "date":"2026-05-22",
              "time":"09:00",
              "customer_name":"Richard",
              "customer_phone":"+573001234567",
              "customer_confirmed":true
            }
            """);

        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("payment_pending");
        _reservations.Verify(r => r.CreateReservationAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDepositNotRequired_CreatesConfirmedReservation()
    {
        _intentBuilder.Setup(b => b.BuildFromContextAsync(It.IsAny<AgentToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationIntentSnapshot(
                Guid.NewGuid(),
                "Plan Marineritos",
                new DateTime(2026, 5, 22, 9, 0, 0),
                60,
                null,
                "Richard",
                null,
                "+573001234567",
                [],
                "{}"));

        _reservations.Setup(r => r.CreateReservationAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResponse(
                Guid.NewGuid(),
                "Plan Marineritos",
                "María",
                new DateOnly(2026, 5, 22),
                new TimeOnly(9, 0),
                60,
                []));

        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""
            {
              "service":"Plan Marineritos",
              "date":"2026-05-22",
              "time":"09:00",
              "customer_name":"Richard",
              "customer_phone":"+573001234567",
              "customer_confirmed":true
            }
            """);

        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"is_booking_confirmed\":true");
        json.Should().NotContain("confirmation_token");
        _reservations.Verify(r => r.CreateReservationAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _paymentLifecycle.Verify(p => p.GetActiveByConversationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDepositNotRequired_DoesNotRegisterConfirmationFragment()
    {
        _intentBuilder.Setup(b => b.BuildFromContextAsync(It.IsAny<AgentToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationIntentSnapshot(
                Guid.NewGuid(),
                "Plan Marineritos",
                new DateTime(2026, 5, 22, 9, 0, 0),
                60,
                null,
                "Richard",
                null,
                "+573001234567",
                [],
                "{}"));

        _reservations.Setup(r => r.CreateReservationAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResponse(
                Guid.NewGuid(),
                "Plan Marineritos",
                "María",
                new DateOnly(2026, 5, 22),
                new TimeOnly(9, 0),
                60,
                []));

        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var ctx = CreateContext();
        ctx.Turn = turn;

        using var args = JsonDocument.Parse("""
            {
              "service":"Plan Marineritos",
              "date":"2026-05-22",
              "time":"09:00",
              "customer_name":"Richard",
              "customer_phone":"+573001234567",
              "customer_confirmed":true
            }
            """);

        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        turn.FragmentEntries.Should().BeEmpty();
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
