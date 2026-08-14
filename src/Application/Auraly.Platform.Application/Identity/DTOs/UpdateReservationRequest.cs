using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public record UpdateReservationRequest(
    Guid? ServiceId,
    Guid? EmployeeId,
    DateTime? ReservationDateTime,
    int? DurationMinutes,
    ReservationStatus? Status);
