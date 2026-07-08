using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
using MimosBabySpa.Domain.Repositories;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class CreateReservationToolTests
{
    private readonly Mock<IReservationService> _reservations = new();
    private readonly Mock<IReservationIntentBuilder> _intentBuilder = new();
    private readonly Mock<IBusinessRuleEngine> _rules = new();
    private readonly Mock<IAvailabilityService> _availability = new();
    private readonly Mock<ISchedulingPolicyProvider> _schedulingPolicy = new();
    private readonly Mock<IServiceRepository> _services = new();
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

        _services.Setup(r => r.GetActiveByBusinessIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<Service>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(_services.Object);
        var serviceNameResolver = new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);

        _tool = new CreateReservationTool(
            _reservations.Object,
            _intentBuilder.Object,
            _rules.Object,
            _availability.Object,
            _schedulingPolicy.Object,
            serviceNameResolver,
            NullLogger<CreateReservationTool>.Instance);
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
                "Maria",
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
                "Maria",
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

    [Fact]
    public async Task ExecuteAsync_WhenServiceArgumentHasDiacritics_CreatesReservationWithCanonicalService()
    {
        _services.Setup(r => r.GetActiveByBusinessIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync([
                new Service
                {
                    ServiceId = Guid.NewGuid(),
                    BusinessId = Guid.NewGuid(),
                    ServiceName = "Diseno Interior",
                    DurationMinutes = 60,
                    IsActive = true
                }
            ]);

        _intentBuilder.Setup(b => b.BuildFromContextAsync(It.IsAny<AgentToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationIntentSnapshot(
                Guid.NewGuid(),
                "Diseno Interior",
                new DateTime(2026, 5, 22, 9, 0, 0),
                60,
                null,
                "Ana Gomez",
                null,
                "+573001234567",
                [],
                "{}"));

        _reservations.Setup(r => r.CreateReservationAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResponse(
                Guid.NewGuid(),
                "Diseno Interior",
                string.Empty,
                new DateOnly(2026, 5, 22),
                new TimeOnly(9, 0),
                60,
                []));

        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""
            {
              "service":"Dise\u00f1o Interior",
              "date":"2026-05-22",
              "time":"09:00",
              "customer_name":"Ana Gomez",
              "customer_phone":"+573001234567",
              "customer_confirmed":true
            }
            """);

        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _rules.Verify(r => r.ValidateReservationAsync(
            It.IsAny<Guid>(),
            "Diseno Interior",
            It.IsAny<DateOnly>(),
            It.IsAny<TimeOnly>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _reservations.Verify(r => r.CreateReservationAsync(
            It.Is<CreateReservationRequest>(request => request.ServiceName == "Diseno Interior"),
            It.IsAny<CancellationToken>()), Times.Once);
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
