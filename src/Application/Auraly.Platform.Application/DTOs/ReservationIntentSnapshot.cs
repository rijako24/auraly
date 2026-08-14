namespace Auraly.Platform.Application.DTOs;

/// <summary>
/// Snapshot inmutable del intent de reserva capturado al generar el link de pago
/// o al confirmar verbalmente. Universal queryable + JSON custom por tenant.
/// </summary>
public sealed record ReservationIntentSnapshot(
    Guid ServiceId,
    string ServiceName,
    DateTime ReservationDateTime,
    int DurationMinutes,
    Guid? PreferredEmployeeId,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    IReadOnlyList<Guid> AddOnServiceIds,
    string? CustomAttributesJson);
