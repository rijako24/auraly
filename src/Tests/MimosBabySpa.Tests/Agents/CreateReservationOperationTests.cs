using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Reservation;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CreateReservationOperationTests
{
    [Fact]
    public async Task ExecuteAsync_WithConfirmedFacts_ReturnsCreatedOutcomeAndDomainEvent()
    {
        var reservationId = Guid.NewGuid();
        var service = new Mock<IReservationCreationService>();
        service.Setup(value => value.CreateAsync(
                It.Is<ReservationCreationRequest>(request =>
                    request.CustomerConfirmed
                    && request.Service == "Corte"
                    && request.CustomerPhone == "+573001112233"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationCreationResult
            {
                Success = true,
                Code = ReservationCreationOutcomeCodes.Created,
                ReservationId = reservationId,
                Service = "Corte",
                Date = "2026-07-12",
                Time = "09:00",
                CustomerName = "Richard",
                CustomerPhone = "+573001112233",
                IsBookingConfirmed = true
            });
        var operation = new CreateReservationOperation(service.Object);
        using var input = Input(confirmed: true);

        var outcome = await operation.ExecuteAsync(input.RootElement, Context(), CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(ReservationCreationOutcomeCodes.Created);
        outcome.Data.GetProperty("reservationId").GetGuid().Should().Be(reservationId);
        outcome.Events.Should().ContainSingle().Which.Should().Be("reservation_created");
        var domainEvent = outcome.DomainEvents.Should().ContainSingle().Subject;
        domainEvent.Name.Should().Be("reservation_created");
        domainEvent.Payload.GetProperty("reservationId").GetGuid().Should().Be(reservationId);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPaymentIsPending_ReturnsRecoverableTypedFailure()
    {
        var service = new Mock<IReservationCreationService>();
        service.Setup(value => value.CreateAsync(
                It.IsAny<ReservationCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationCreationResult.Fail(
                ReservationCreationOutcomeCodes.PaymentRequired,
                "Payment pending.",
                true));
        var operation = new CreateReservationOperation(service.Object);
        using var input = Input(confirmed: true);

        var outcome = await operation.ExecuteAsync(input.RootElement, Context(), CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.Code.Should().Be(ReservationCreationOutcomeCodes.PaymentRequired);
        outcome.Error!.Recoverable.Should().BeTrue();
        outcome.Error.RemediationSignal.Should().Be("payment.await_confirmation");
        outcome.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdempotent_DoesNotEmitReservationCreatedAgain()
    {
        var service = new Mock<IReservationCreationService>();
        service.Setup(value => value.CreateAsync(
                It.IsAny<ReservationCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationCreationResult
            {
                Success = true,
                Code = ReservationCreationOutcomeCodes.IdempotentReplay,
                ReservationId = Guid.NewGuid(),
                Service = "Corte",
                Date = "2026-07-12",
                Time = "09:00",
                CustomerName = "Richard",
                IsBookingConfirmed = true,
                IdempotentReplay = true
            });
        var operation = new CreateReservationOperation(service.Object);
        using var input = Input(confirmed: true);

        var outcome = await operation.ExecuteAsync(input.RootElement, Context(), CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(ReservationCreationOutcomeCodes.IdempotentReplay);
        outcome.Events.Should().BeEmpty();
        outcome.DomainEvents.Should().BeEmpty();
        outcome.Data.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();
    }

    private static JsonDocument Input(bool confirmed) => JsonDocument.Parse($$"""
        {
          "service": "Corte",
          "date": "2026-07-12",
          "time": "09:00",
          "customer_name": "Richard",
          "customer_phone": "+573001112233",
          "add_ons": "ninguno",
          "customer_confirmed": {{confirmed.ToString().ToLowerInvariant()}}
        }
        """);

    private static OperationContext Context() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        BusinessToday = new DateOnly(2026, 7, 10),
        BusinessNow = new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.FromHours(-5)),
        Config = new AgentConfig(),
        ConversationState = new ConversationState(),
        Facts = new Dictionary<string, string>()
    };
}
