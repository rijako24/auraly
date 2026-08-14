namespace Auraly.Platform.Domain.Enums;

public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    PendingCalendar = 4,
    OnHold = 5
}

public static class ReservationStatusExtensions
{
    public static bool BlocksAvailability(this ReservationStatus status) =>
        status is ReservationStatus.Confirmed
            or ReservationStatus.Completed
            or ReservationStatus.OnHold
            or ReservationStatus.PendingCalendar;
}
