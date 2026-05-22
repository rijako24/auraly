namespace MimosBabySpa.Application.DTOs;

/// <summary>
/// Request para crear una reserva. Contiene los datos crudos del estado conversacional.
/// Los add-ons y demás datos de negocio van en <see cref="BusinessAttributes"/>
/// (clave <see cref="ReservationBusinessAttributeKeys.SelectedAddOns"/>); el servicio resuelve nombres a IDs.
/// </summary>
public record CreateReservationRequest(
    Guid BusinessId,
    Guid ConversationId,
    string ServiceName,
    DateOnly Date,
    TimeOnly Time,
    string? CustomerName,
    string? Email,
    string? Phone,
    IReadOnlyDictionary<string, string> BusinessAttributes,
    string? CustomAttributesJson = null);
