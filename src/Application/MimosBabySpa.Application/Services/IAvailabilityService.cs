using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
///
/// La duración del servicio se obtiene directamente del catálogo (tabla Services).
/// Los parámetros de agendamiento (horarios, intervalo de slots, buffer, estrategia de
/// empleado) se reciben del nodo que invoca el servicio vía <see cref="AvailabilityParams"/>,
/// sin leer ninguna clave de BusinessConfigurations.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad.
    /// Con <paramref name="time"/>: revisa ese slot exacto.
    /// Sin <paramref name="time"/>: devuelve todos los slots disponibles del día
    /// (empleado + recursos verificados).
    /// </summary>
    Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        AvailabilityParams? policy = null,
        CancellationToken cancellationToken = default);
}
