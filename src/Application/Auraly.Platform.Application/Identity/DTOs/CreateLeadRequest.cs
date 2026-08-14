namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateLeadRequest(
    Guid BusinessId,
    string UserNumber,
    string? CustomerName,
    string? Notes);
