using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ReservationLifecycleServiceTests
{
    [Fact]
    public async Task ResolveForSessionAsync_WhenConversationReservationIsPast_DoesNotReturnItAsManageable()
    {
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetManageableByConversationIdAsync(
                conversationId,
                new DateOnly(2026, 6, 17),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
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
    public async Task ResolveForSessionAsync_WhenConversationHasMultipleReservations_ReturnsAllManageable()
    {
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var first = CreateReservation(conversationId, businessId, new DateTime(2026, 6, 17, 10, 0, 0));
        var second = CreateReservation(conversationId, businessId, new DateTime(2026, 6, 18, 11, 0, 0));

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetManageableByConversationIdAsync(
                conversationId,
                new DateOnly(2026, 6, 17),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([second, first]);
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

        result.ManageableReservations.Should().Equal(first, second);
    }

    [Fact]
    public async Task ResolveForSessionAsync_WhenSameReservationAppearsByPhoneAndConversation_Deduplicates()
    {
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var reservation = CreateReservation(conversationId, businessId, new DateTime(2026, 6, 17, 10, 0, 0));

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.GetManageableByConversationIdAsync(
                conversationId,
                new DateOnly(2026, 6, 17),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation]);
        reservations.Setup(r => r.GetManageableByCustomerPhoneAsync(
                businessId,
                "573001112233",
                new DateOnly(2026, 6, 17),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation]);

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
    }

    private static Reservation CreateReservation(Guid conversationId, Guid businessId, DateTime dateTime) =>
        new()
        {
            ReservationId = Guid.NewGuid(),
            ConversationId = conversationId,
            BusinessId = businessId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = dateTime
        };
}
