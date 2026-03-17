namespace MimosBabySpa.Application.Identity.DTOs;

public record LeadDto(
    Guid LeadId,
    Guid BusinessId,
    string UserNumber,
    string Status,
    DateTime Timestamp,
    string? CustomerName,
    string? Notes);
