using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public record ReservationDto(
    Guid ReservationId,
    Guid BusinessId,
    Guid? ServiceId,
    string ServiceName,
    Guid? EmployeeId,
    string EmployeeName,
    DateTime? ReservationDateTime,
    int? DurationMinutes,
    ReservationStatus Status,
    DateTime CreatedAt);
