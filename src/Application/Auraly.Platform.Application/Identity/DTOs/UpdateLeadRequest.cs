namespace Auraly.Platform.Application.Identity.DTOs;

public record UpdateLeadRequest(
    string? Status,
    string? CustomerName,
    string? Notes);
