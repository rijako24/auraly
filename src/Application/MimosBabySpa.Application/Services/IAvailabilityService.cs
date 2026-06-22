using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinÃ­stica.
///
/// La duraciÃ³n del servicio se obtiene directamente del catÃ¡logo (tabla Services).
/// Los parÃ¡metros de agendamiento (horarios, intervalo de slots, buffer, estrategia de
/// empleado) se reciben del nodo que invoca el servicio vÃ­a <see cref="AvailabilityParams"/>,
/// sin leer ninguna clave de configuraciones legacy.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad.
    /// Con <paramref name="time"/>: revisa ese slot exacto.
    /// Sin <paramref name="time"/>: devuelve todos los slots disponibles del dÃ­a
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

