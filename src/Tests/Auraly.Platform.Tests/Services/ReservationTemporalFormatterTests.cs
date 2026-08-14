using FluentAssertions;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ReservationTemporalFormatterTests
{
    [Fact]
    public void FormatLine_WhenReservationIsToday_IncludesCurrentRelativeLabel()
    {
        var reservation = new Reservation
        {
            ReservationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReservationDateTime = new DateTime(2026, 6, 17, 10, 0, 0),
            Service = new Service { ServiceName = "Hidroterapia" },
            Status = ReservationStatus.Confirmed
        };

        var result = ReservationTemporalFormatter.FormatLine(
            reservation,
            new DateOnly(2026, 6, 17));

        result.Should().Contain("2026-06-17 10:00 Hidroterapia, hoy");
        result.Should().NotContain("id_reserva");
        result.Should().NotContain("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void FormatRelativeLabel_WhenReservationIsTomorrow_ReturnsTomorrowFromTurnDate()
    {
        var result = ReservationTemporalFormatter.FormatRelativeLabel(
            new DateOnly(2026, 6, 18),
            new DateOnly(2026, 6, 17));

        result.Should().Be("ma\u00f1ana");
    }

    [Fact]
    public void IsManageableOnBusinessDay_WhenReservationIsPast_ReturnsFalse()
    {
        var reservation = new Reservation
        {
            ReservationDateTime = new DateTime(2026, 6, 16, 10, 0, 0),
            Status = ReservationStatus.Confirmed
        };

        var result = ReservationTemporalFormatter.IsManageableOnBusinessDay(
            reservation,
            new DateOnly(2026, 6, 17));

        result.Should().BeFalse();
    }
}
