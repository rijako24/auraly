namespace Auraly.Platform.Application.Identity.DTOs;

public record UpdateEmployeeRequest(
    string? Name,
    bool? IsActive,
    IReadOnlyList<Guid>? ServiceIds);
