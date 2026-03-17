using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record UpdateReservationRequest(
    Guid? ServiceId,
    Guid? EmployeeId,
    DateTime? ReservationDateTime,
    int? DurationMinutes,
    ReservationStatus? Status);
