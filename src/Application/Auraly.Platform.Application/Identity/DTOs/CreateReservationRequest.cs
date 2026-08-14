namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateReservationRequest(
    Guid BusinessId,
    Guid ServiceId,
    Guid EmployeeId,
    DateTime ReservationDateTime,
    int? DurationMinutes);
