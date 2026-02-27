namespace MimosBabySpa.Application.DTOs;

/// <summary>
/// Request para crear una reserva. Contiene los datos crudos del estado conversacional.
/// El servicio es responsable de resolver nombres a IDs y validar.
/// </summary>
public record CreateReservationRequest(
    Guid BusinessId,
    Guid ConversationId,
    string ServiceName,
    DateOnly Date,
    TimeOnly Time,
    string? SelectedAddOnsCsv,
    string? CustomerName,
    string? Email,
    string? Phone,
    IReadOnlyDictionary<string, string> BusinessAttributes);
