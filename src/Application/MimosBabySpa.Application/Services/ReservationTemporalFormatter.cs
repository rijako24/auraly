using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public static class ReservationTemporalFormatter
{
    private static readonly string[] WeekdayNames =
    [
        "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado"
    ];

    public static string FormatLine(Reservation reservation, DateOnly businessToday)
    {
        var service = reservation.Service?.ServiceName ?? reservation.GetServiceName() ?? "servicio";
        if (!reservation.ReservationDateTime.HasValue)
            return service;

        var reservationDate = DateOnly.FromDateTime(reservation.ReservationDateTime.Value);
        var time = TimeOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("HH:mm");
        var relative = FormatRelativeLabel(reservationDate, businessToday);
        var suffix = string.IsNullOrWhiteSpace(relative) ? string.Empty : $", {relative}";

        return $"{reservationDate:yyyy-MM-dd} {time} {service}{suffix}";
    }

    public static string FormatRelativeLabel(DateOnly date, DateOnly businessToday)
    {
        var delta = date.DayNumber - businessToday.DayNumber;
        return delta switch
        {
            0 => "hoy",
            1 => "ma\u00f1ana",
            2 => "pasado ma\u00f1ana",
            >= 3 and <= 6 => WeekdayNames[(int)date.DayOfWeek],
            _ => string.Empty
        };
    }

    public static bool IsManageableOnBusinessDay(Reservation reservation, DateOnly businessToday) =>
        reservation.Status is Domain.Enums.ReservationStatus.Confirmed or Domain.Enums.ReservationStatus.OnHold
        && (!reservation.ReservationDateTime.HasValue
            || DateOnly.FromDateTime(reservation.ReservationDateTime.Value) >= businessToday);
}
