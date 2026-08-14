namespace Auraly.Platform.Application.DTOs;

/// <summary>
/// Respuesta de creación de reserva. Contiene todos los datos necesarios
/// para construir el mensaje de éxito y actualizar el estado.
/// </summary>
public record CreateReservationResponse(
    Guid ReservationId,
    string ServiceName,
    string EmployeeName,
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    IReadOnlyList<string> AddOnNames);
