namespace MimosBabySpa.Domain.Enums;

public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    PendingCalendar = 4, // Reserva creada pero evento de calendario falló
    OnHold = 5 // "No puede asistir" / "avisa después" — excluida de disponibilidad
}
