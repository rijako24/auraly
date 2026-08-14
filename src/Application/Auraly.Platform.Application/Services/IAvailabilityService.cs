using Auraly.Platform.Application.Configuration;

namespace Auraly.Platform.Application.Services;

/// <summary>
/// Servicio para calcular disponibilidad de forma determinística.
///
/// La duración del servicio se obtiene directamente del catálogo (tabla Services).
/// Los parámetros de agendamiento (horarios, intervalo, buffer, estrategia de
/// empleado) se reciben del nodo que invoca el servicio vía <see cref="AvailabilityParams"/>,
/// sin leer ninguna clave de configuraciones legacy.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Verifica disponibilidad.
    /// Con <paramref name="time"/>: revisa ese inicio exacto.
    /// Sin <paramref name="time"/>: devuelve todas las opciones reservables del día
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

