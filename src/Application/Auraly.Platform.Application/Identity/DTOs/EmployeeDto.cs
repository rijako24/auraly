namespace Auraly.Platform.Application.Identity.DTOs;

public record EmployeeDto(
    Guid EmployeeId,
    Guid BusinessId,
    Guid? PartyId,
    string Name,
    bool IsActive,
    IReadOnlyList<Guid> ServiceIds,
    DateTime CreatedAt);
