namespace MimosBabySpa.Application.Identity.DTOs;

public record EmployeeDto(
    Guid EmployeeId,
    Guid BusinessId,
    string Name,
    bool IsActive,
    IReadOnlyList<Guid> ServiceIds,
    DateTime CreatedAt);
