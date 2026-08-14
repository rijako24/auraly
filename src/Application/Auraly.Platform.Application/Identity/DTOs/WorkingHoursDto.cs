namespace Auraly.Platform.Application.Identity.DTOs;

public record WorkingHourDto(
    Guid? WorkingHourId,
    int DayOfWeek,
    string OpenTime,
    string CloseTime,
    bool IsActive);

public record UpdateWorkingHoursRequest(IReadOnlyList<WorkingHourDto> WorkingHours);

public record EmployeeWorkingHoursDto(
    Guid EmployeeId,
    bool UsesBusinessFallback,
    IReadOnlyList<WorkingHourDto> WorkingHours);

public record BusinessAvailabilityBlockDto(
    Guid BusinessAvailabilityBlockId,
    Guid BusinessId,
    Guid? EmployeeId,
    string? EmployeeName,
    string Date,
    string? StartTime,
    string? EndTime,
    string Reason,
    string Source,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record UpsertBusinessAvailabilityBlockRequest(
    Guid? EmployeeId,
    string Date,
    string? StartTime,
    string? EndTime,
    string? Reason,
    bool IsActive);

public record EmployeeScheduleExceptionDto(
    Guid EmployeeScheduleExceptionId,
    Guid BusinessId,
    Guid EmployeeId,
    string Date,
    string? OpenTime,
    string? CloseTime,
    bool IsClosed,
    string? Reason,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record UpsertEmployeeScheduleExceptionRequest(
    string Date,
    string? OpenTime,
    string? CloseTime,
    bool IsClosed,
    string? Reason);
