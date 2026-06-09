namespace MimosBabySpa.Domain.Enums;

public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    PendingCalendar = 4,
    OnHold = 5,

    /// <summary>Borrador en curso — obsoleto; ya no se crean drafts.</summary>
    [Obsolete("Draft reservations are no longer created. Intent lives in facts or PaymentTransactions snapshot.")]
    Draft = 10,

    /// <summary>Slot verificado — obsoleto.</summary>
    [Obsolete("Use PaymentTransactions snapshot instead of draft reservations.")]
    AvailabilityVerified = 11,

    /// <summary>Esperando pago — obsoleto.</summary>
    [Obsolete("Payment intent is tracked in PaymentTransactions without a Reservation.")]
    PendingPayment = 12,

    /// <summary>Borrador expirado sin confirmar.</summary>
    Expired = 91
}

public static class ReservationStatusExtensions
{
    public static bool IsActiveDraft(this ReservationStatus status) =>
        status is ReservationStatus.Draft
            or ReservationStatus.AvailabilityVerified
            or ReservationStatus.PendingPayment
            or ReservationStatus.Pending;

    public static bool BlocksAvailability(this ReservationStatus status) =>
        status is ReservationStatus.Confirmed
            or ReservationStatus.Completed
            or ReservationStatus.OnHold
            or ReservationStatus.PendingCalendar;
}
