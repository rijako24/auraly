namespace Auraly.Platform.Application.Identity.DTOs;

public record TopServiceDto(
    Guid ServiceId,
    string ServiceName,
    int TotalReservations,
    decimal Revenue);
