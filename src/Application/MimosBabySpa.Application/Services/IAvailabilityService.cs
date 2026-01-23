namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
/// Toda la lógica de negocio sobre capacidad y conflictos está aquí, NO en el modelo.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad para un servicio en una fecha y hora específica.
    /// Retorna información explícita: is_available, max_capacity, current_reservations, etc.
    /// </summary>
    Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        int? durationMinutes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado determinístico de verificación de disponibilidad.
/// El modelo NO debe inferir esto, solo usar estos valores.
/// </summary>
public class AvailabilityResult
{
    public bool IsAvailable { get; set; }
    public int MaxCapacity { get; set; } = 1; // Por defecto 1 servicio por horario
    public int CurrentReservations { get; set; }
    public List<BookedSlot> BookedSlots { get; set; } = new();
    public List<BookedSlot> OverlappingSlots { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class BookedSlot
{
    public string Time { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string Service { get; set; } = string.Empty;
}
