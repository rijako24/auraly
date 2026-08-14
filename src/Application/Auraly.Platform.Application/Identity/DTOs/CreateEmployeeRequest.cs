namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateEmployeeRequest(
    Guid BusinessId,
    string Name,
    IReadOnlyList<Guid>? ServiceIds,
    Guid? PartyId = null);
