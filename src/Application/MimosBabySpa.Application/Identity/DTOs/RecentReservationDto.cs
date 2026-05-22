namespace MimosBabySpa.Application.Identity.DTOs;

public record RecentReservationDto(
    Guid ReservationId,
    DateTime? ReservationDateTime,
    string ServiceName,
    string? CustomerName,
    string Status,
    decimal Price);
