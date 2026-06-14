namespace MimosBabySpa.Application.Identity.DTOs;

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
