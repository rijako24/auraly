namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
/// Toda la lógica de negocio sobre capacidad y conflictos está aquí, NO en el modelo.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad. Con hora: revisa ese slot. Sin hora: consulta el horario del negocio y revisa cada hora abierta (empleado + recursos).
    /// </summary>
    Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        int? durationMinutes,
        CancellationToken cancellationToken = default);
}
