using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class CustomerReservationResolverTests
{
    [Fact]
    public async Task ResolveAsync_WithMultipleReservationsAndNoUserIdentifier_ReturnsAmbiguous()
    {
        var fx = new Fixture();
        fx.Context.ManageableReservations =
        [
            fx.CreateReservation(new DateTime(2026, 9, 1, 11, 0, 0), "Corte basico adulto"),
            fx.CreateReservation(new DateTime(2026, 9, 6, 10, 0, 0), "Corte premium adulto")
        ];
        fx.Context.LatestUserMessage = "quiero cambiar mi reserva a las 12:00";

        var result = await fx.Resolver.ResolveAsync(fx.Context, null);

        result.Success.Should().BeFalse();
        result.ErrorJson.Should().Contain("ambiguous_reservation");
        result.ErrorJson.Should().Contain("select_reservation");
        result.ErrorJson.Should().NotContain("id_reserva");
    }

    [Fact]
    public async Task ResolveAsync_WithMultipleReservationsAndExistingDateMention_ReturnsMatchingReservation()
    {
        var fx = new Fixture();
        var first = fx.CreateReservation(new DateTime(2026, 9, 1, 11, 0, 0), "Corte basico adulto");
        var second = fx.CreateReservation(new DateTime(2026, 9, 6, 10, 0, 0), "Corte premium adulto");
        fx.Context.ManageableReservations = [first, second];
        fx.Context.LatestUserMessage = "cambia la reserva del 6 de septiembre a las 12:00";

        var result = await fx.Resolver.ResolveAsync(fx.Context, null);

        result.Success.Should().BeTrue();
        result.Reservation.Should().BeSameAs(second);
    }

    [Fact]
    public async Task ResolveAsync_NonUuidReservationId_FallsBackToContextResolution()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation(new DateTime(2026, 9, 6, 10, 0, 0), "Corte basico adulto");
        fx.Context.ManageableReservations = [reservation];
        fx.Context.LatestUserMessage = "quiero cambiar el servicio de mi reserva";

        var result = await fx.Resolver.ResolveAsync(fx.Context, "2026-09-06 10:00 Corte basico adulto");

        result.Success.Should().BeTrue();
        result.Reservation.Should().BeSameAs(reservation);
    }
    [Fact]
    public async Task ResolveAsync_ExplicitReservationFromSameConversation_AllowsDifferentContactPhoneSnapshot()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation(new DateTime(2026, 9, 6, 10, 0, 0), "Corte basico adulto");
        reservation.CustomerPhoneSnapshot = "3002002000";
        fx.Reservations.Setup(r => r.GetByIdAsync(reservation.ReservationId)).ReturnsAsync(reservation);

        var result = await fx.Resolver.ResolveAsync(fx.Context, reservation.ReservationId.ToString("D"));

        result.Success.Should().BeTrue();
        result.Reservation.Should().BeSameAs(reservation);
    }

    private sealed class Fixture
    {
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid ConversationId { get; } = Guid.NewGuid();
        public Mock<IReservationRepository> Reservations { get; } = new();
        public CustomerReservationResolver Resolver { get; }
        public AgentConversationContext Context { get; }

        public Fixture()
        {
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(u => u.Reservations).Returns(Reservations.Object);

            var lifecycle = new Mock<IReservationLifecycleService>();
            lifecycle.Setup(l => l.ResolveForSessionAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CustomerReservationSession.None);

            Resolver = new CustomerReservationResolver(unitOfWork.Object, lifecycle.Object);
            Context = new AgentConversationContext
            {
                BusinessId = BusinessId,
                ConversationId = ConversationId,
                ChannelPhone = "+15550800200",
                BusinessToday = new DateOnly(2026, 7, 7),
                Conversation = new Conversation()
            };
        }

        public Reservation CreateReservation(DateTime dateTime, string serviceName) =>
            new()
            {
                ReservationId = Guid.NewGuid(),
                BusinessId = BusinessId,
                ConversationId = ConversationId,
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = dateTime,
                Service = new Service { ServiceName = serviceName },
                CustomerPhoneSnapshot = "+15550800200"
            };
    }
}
