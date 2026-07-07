using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class ReservationPaidCheckoutFulfillmentHandlerTests
{
    [Fact]
    public async Task FulfillAsync_WhenMatchingReservationAlreadyExists_LinksPaymentWithoutCheckingAvailability()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var reservationDateTime = new DateTime(2026, 7, 7, 8, 30, 0);

        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            BusinessId = businessId,
            ConversationId = conversationId,
            PaymentReferenceId = "test_Mx7sFE",
            AmountInCents = 2500000,
            Currency = "COP",
            Status = PaymentTransactionStatus.Confirmed,
            CheckoutKind = CheckoutKind.Reservation,
            CheckoutSnapshotJson = $$"""
            {
              "kind": "Reservation",
              "service_id": "{{serviceId}}",
              "service_name": "Peinado premium",
              "duration_minutes": 20,
              "payer_name": "Leonardo Policia",
              "payment_phone": "573124185180",
              "reservation_date": "2026-07-07",
              "reservation_time": "08:30"
            }
            """
        };

        var existingReservation = new Reservation
        {
            ReservationId = reservationId,
            BusinessId = businessId,
            ConversationId = conversationId,
            ServiceId = serviceId,
            ReservationDateTime = reservationDateTime,
            Status = ReservationStatus.Confirmed,
            CustomerPhoneSnapshot = "573124185180"
        };

        var services = new Mock<IServiceRepository>();
        services.Setup(r => r.GetByIdAsync(serviceId))
            .ReturnsAsync(new Service { ServiceId = serviceId, ServiceName = "Peinado premium" });

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetActiveByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingReservation);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(services.Object);
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservations.Object);

        var lifecycle = new Mock<IPaymentLifecycleService>();
        var availability = new Mock<IAvailabilityService>(MockBehavior.Strict);
        var scheduling = new Mock<ISchedulingPolicyProvider>(MockBehavior.Strict);

        var handler = new ReservationPaidCheckoutFulfillmentHandler(
            unitOfWork.Object,
            lifecycle.Object,
            Mock.Of<IReservationService>(),
            availability.Object,
            scheduling.Object,
            NullLogger<ReservationPaidCheckoutFulfillmentHandler>.Instance);

        var result = await handler.FulfillAsync(payment, null, new AgentConfig { BusinessId = businessId });

        result.CompletionReason.Should().Be("payment_reservation_already_created");
        result.TargetId.Should().Be(reservationId);
        result.NotifyCustomer.Should().BeFalse();
        lifecycle.Verify(l => l.LinkReservationAsync(payment, reservationId, It.IsAny<CancellationToken>()), Times.Once);
        lifecycle.Verify(l => l.MarkRequiresReschedulingAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        availability.VerifyNoOtherCalls();
        scheduling.VerifyNoOtherCalls();
    }
}
