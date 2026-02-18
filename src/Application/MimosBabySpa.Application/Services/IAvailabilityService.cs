namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
/// La duración del servicio se obtiene directamente del catálogo (tabla Services),
/// no se recibe como parámetro — el llamador no tiene por qué conocerla.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad. Con hora: revisa ese slot. Sin hora: consulta el horario del
    /// negocio y devuelve todos los slots disponibles (empleado + recursos verificados).
    /// </summary>
    Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        CancellationToken cancellationToken = default);
}
