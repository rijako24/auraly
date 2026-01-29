namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
/// Toda la lógica de negocio sobre capacidad y conflictos está aquí, NO en el modelo.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad para un servicio en una fecha y hora específica.
    /// Retorna información explícita: is_available, current_reservations, etc.
    /// </summary>
    Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        int? durationMinutes,
        CancellationToken cancellationToken = default);
}
