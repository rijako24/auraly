namespace Auraly.Platform.Application.Models;

/// <summary>
/// DTO que representa el contexto actual de una conversación para el detector de intención.
/// </summary>
public class ConversationContextDto
{
    /// <summary>
    /// Servicio seleccionado por el usuario (si existe).
    /// </summary>
    public string? Service { get; set; }

    /// <summary>
    /// Fecha deseada por el usuario (si existe).
    /// </summary>
    public string? DesiredDate { get; set; }

    /// <summary>
    /// Hora deseada por el usuario (si existe).
    /// </summary>
    public string? DesiredTime { get; set; }

    /// <summary>
    /// Resultado de la última verificación de disponibilidad.
    /// true = disponible, false = no disponible, null = no se ha verificado aún.
    /// </summary>
    public bool? LastAvailabilityResult { get; set; }

    /// <summary>
    /// Fecha/hora de la última verificación de disponibilidad (si existe).
    /// </summary>
    public DateTime? LastAvailabilityCheckDate { get; set; }

    /// <summary>
    /// Hora de la última verificación de disponibilidad (si existe).
    /// </summary>
    public TimeSpan? LastAvailabilityCheckTime { get; set; }
}
