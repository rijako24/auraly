using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class ReservationLifecycleServiceTests
{
    [Fact]
    public async Task ResolveForSessionAsync_WhenConversationReservationIsPast_DoesNotReturnItAsManageable()
    {
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetActiveByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Reservation
            {
                ConversationId = conversationId,
                BusinessId = businessId,
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = new DateTime(2026, 6, 16, 10, 0, 0)
            });
        reservations.Setup(r => r.GetManageableByCustomerPhoneAsync(
                businessId,
                "573001112233",
                new DateOnly(2026, 6, 17),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservations.Object);

        var service = new ReservationLifecycleService(unitOfWork.Object);

        var result = await service.ResolveForSessionAsync(
            conversationId,
            businessId,
            "573001112233",
            new DateOnly(2026, 6, 17),
            CancellationToken.None);

        result.ManageableReservations.Should().BeEmpty();
        reservations.Verify(r => r.GetManageableByCustomerPhoneAsync(
            businessId,
            "573001112233",
            new DateOnly(2026, 6, 17),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveForSessionAsync_WhenConversationReservationIsToday_ReturnsItAsManageable()
    {
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var reservation = new Reservation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = new DateTime(2026, 6, 17, 10, 0, 0)
        };

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetActiveByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservations.Object);

        var service = new ReservationLifecycleService(unitOfWork.Object);

        var result = await service.ResolveForSessionAsync(
            conversationId,
            businessId,
            "573001112233",
            new DateOnly(2026, 6, 17),
            CancellationToken.None);

        result.ManageableReservations.Should().ContainSingle().Which.Should().BeSameAs(reservation);
        reservations.Verify(r => r.GetManageableByCustomerPhoneAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
