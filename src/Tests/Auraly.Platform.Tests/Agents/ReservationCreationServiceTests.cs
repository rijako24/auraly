using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations.Reservation;
using Auraly.Platform.Application.BusinessRules;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class ReservationCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_WithoutCustomerConfirmation_ReturnsSummaryWithoutSideEffects()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CreateAsync(
            Request(fixture.BusinessId, confirmed: false),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Code.Should().Be("reservation.pending_confirmation");
        result.IsBookingConfirmed.Should().BeFalse();
        fixture.Reservations.Verify(value => value.CreateReservationAsync(
            It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Rules.Verify(value => value.ValidateReservationAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithPendingPayment_DoesNotCreateDuplicateReservation()
    {
        var fixture = CreateFixture();
        fixture.Payments.Setup(value => value.GetActiveByConversationAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentTransaction { Status = PaymentTransactionStatus.Created });

        var result = await fixture.Service.CreateAsync(
            Request(fixture.BusinessId, confirmed: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("payment.required");
        result.Recoverable.Should().BeTrue();
        fixture.Reservations.Verify(value => value.CreateReservationAsync(
            It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithMatchingExistingReservation_IsIdempotent()
    {
        var fixture = CreateFixture();
        var existing = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = new DateTime(2026, 7, 12, 9, 0, 0)
        };
        fixture.Lifecycle.Setup(value => value.GetActiveAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await fixture.Service.CreateAsync(
            Request(fixture.BusinessId, confirmed: true),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Code.Should().Be("reservation.idempotent_replay");
        result.IdempotentReplay.Should().BeTrue();
        result.ReservationId.Should().Be(existing.ReservationId);
        fixture.Reservations.Verify(value => value.CreateReservationAsync(
            It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenSlotChangedAfterCheckout_RechecksAndRejectsUnavailableSlot()
    {
        var fixture = CreateFixture();
        fixture.Availability.Setup(value => value.CheckAvailabilityAsync(
                It.IsAny<Guid>(),
                "Plan Marineritos",
                It.IsAny<DateTime>(),
                new TimeSpan(9, 0, 0),
                It.IsAny<AvailabilityParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult
            {
                IsAvailable = false,
                ResponseMessage = "Horario ocupado"
            });

        var result = await fixture.Service.CreateAsync(
            Request(fixture.BusinessId, confirmed: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("reservation.slot_unavailable");
        fixture.Reservations.Verify(value => value.CreateReservationAsync(
            It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithValidConfirmedRequest_CreatesReservationAndReturnsNotificationContext()
    {
        var fixture = CreateFixture();
        var reservationId = Guid.NewGuid();
        fixture.Intent.Setup(value => value.BuildAsync(
                It.Is<ReservationIntentContext>(context =>
                    context.Facts["service"] == "Plan Marineritos"
                    && context.Facts["customer_phone"] == "+573001234567"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationIntentSnapshot(
                fixture.ServiceId,
                "Plan Marineritos",
                new DateTime(2026, 7, 12, 9, 0, 0),
                60,
                null,
                "Richard",
                null,
                "+573001234567",
                [],
                "{}"));
        fixture.Reservations.Setup(value => value.CreateReservationAsync(
                It.Is<CreateReservationRequest>(request =>
                    request.ServiceName == "Plan Marineritos"
                    && request.Phone == "+573001234567"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResponse(
                reservationId,
                "Plan Marineritos",
                "Maria",
                new DateOnly(2026, 7, 12),
                new TimeOnly(9, 0),
                60,
                []));

        var result = await fixture.Service.CreateAsync(
            Request(fixture.BusinessId, confirmed: true),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Code.Should().Be("reservation.created");
        result.ReservationId.Should().Be(reservationId);
        result.IsBookingConfirmed.Should().BeTrue();
        result.Reservation.Should().NotBeNull();
        result.Reservation!.Status.Should().Be(ReservationStatus.Confirmed);
    }

    private static CreationFixture CreateFixture()
    {
        var businessId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var catalog = new Service
        {
            BusinessId = businessId,
            ServiceId = serviceId,
            ServiceName = "Plan Marineritos",
            DurationMinutes = 60,
            IsActive = true
        };
        var services = new Mock<IServiceRepository>();
        services.Setup(value => value.GetActiveByBusinessIdAsync(businessId)).ReturnsAsync([catalog]);
        services.Setup(value => value.GetByBusinessIdAndNameAsync(businessId, "Plan Marineritos"))
            .ReturnsAsync(catalog);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Services).Returns(services.Object);
        var names = new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);
        var reservations = new Mock<IReservationService>();
        var intent = new Mock<IReservationIntentBuilder>();
        var rules = new Mock<IBusinessRuleEngine>();
        rules.Setup(value => value.ValidateReservationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });
        var availability = new Mock<IAvailabilityService>();
        availability.Setup(value => value.CheckAvailabilityAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan?>(),
                It.IsAny<AvailabilityParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult { IsAvailable = true });
        var scheduling = new Mock<ISchedulingPolicyProvider>();
        scheduling.Setup(value => value.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailabilityParams.Default);
        var payments = new Mock<IPaymentLifecycleService>();
        var lifecycle = new Mock<IReservationLifecycleService>();
        var service = new ReservationCreationService(
            reservations.Object,
            intent.Object,
            rules.Object,
            availability.Object,
            scheduling.Object,
            names,
            payments.Object,
            lifecycle.Object,
            NullLogger<ReservationCreationService>.Instance);
        return new CreationFixture(
            businessId, serviceId, service, reservations, intent, rules, availability, payments, lifecycle);
    }

    private static ReservationCreationRequest Request(Guid businessId, bool confirmed)
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Role = "booking.service" },
                new FactSchemaEntry { Key = "desired_date", Role = "booking.date" },
                new FactSchemaEntry { Key = "desired_time", Role = "booking.time" },
                new FactSchemaEntry { Key = "customer_name", Role = "customer.name" },
                new FactSchemaEntry { Key = "customer_phone", Role = "customer.phone" },
                new FactSchemaEntry { Key = "add_ons", Role = "booking.addons" }
            ]
        };
        return new ReservationCreationRequest(
            Guid.NewGuid(),
            businessId,
            Guid.NewGuid(),
            new DateOnly(2026, 7, 10),
            config,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Plan Marineritos",
                ["desired_date"] = "2026-07-12",
                ["desired_time"] = "09:00",
                ["customer_name"] = "Richard",
                ["customer_phone"] = "+573001234567",
                ["add_ons"] = "ninguno"
            },
            confirmed);
    }

    private sealed record CreationFixture(
        Guid BusinessId,
        Guid ServiceId,
        ReservationCreationService Service,
        Mock<IReservationService> Reservations,
        Mock<IReservationIntentBuilder> Intent,
        Mock<IBusinessRuleEngine> Rules,
        Mock<IAvailabilityService> Availability,
        Mock<IPaymentLifecycleService> Payments,
        Mock<IReservationLifecycleService> Lifecycle);
}
