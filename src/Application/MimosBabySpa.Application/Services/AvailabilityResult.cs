namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resultado determinístico de verificación de disponibilidad.
/// ResponseMessage es un resumen de estado sin datos de presentación (slots viven en AvailableTimeSlots).
/// No se expone OverlappingSlots/BookedSlots a la IA para no confundirla.
/// </summary>
public class AvailabilityResult
{
    public bool IsAvailable { get; set; }
    public int CurrentReservations { get; set; }
    /// <summary>Horarios realmente disponibles cuando se consulta sin hora (empleado + recursos verificados).</summary>
    public List<string> AvailableTimeSlots { get; set; } = new();

    /// <summary>Resumen de estado para tools (sin listar slots; usar AvailableTimeSlots para datos).</summary>
    public string ResponseMessage { get; set; } = string.Empty;
    /// <summary>Nombre del servicio consultado (contexto de la petición).</summary>
    public string RequestServiceName { get; set; } = string.Empty;
    /// <summary>Fecha consultada en formato YYYY-MM-DD.</summary>
    public string RequestDateString { get; set; } = string.Empty;
    /// <summary>Hora consultada (HH:mm) o null si no se indicó.</summary>
    public string? RequestTimeString { get; set; }
}
