using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Models;

/// <summary>
/// Resultado de la detección de intención del usuario.
/// Contiene toda la información necesaria para controlar el flujo de la conversación.
/// </summary>
public class IntentDetectionResult
{
    /// <summary>
    /// Tipo de intención detectada.
    /// </summary>
    public IntentType Intent { get; set; } = IntentType.Unknown;

    /// <summary>
    /// Indica si se detectó una fecha válida (no narrativa) en el mensaje.
    /// </summary>
    public bool HasDate { get; set; }

    /// <summary>
    /// Indica si se detectó una hora válida en el mensaje.
    /// </summary>
    public bool HasTime { get; set; }

    /// <summary>
    /// Indica si hay un servicio definido en el contexto de la conversación.
    /// </summary>
    public bool HasService { get; set; }

    /// <summary>
    /// Indica si la fecha detectada es narrativa (ej: "el sábado viajo") y no debe usarse para reservas.
    /// </summary>
    public bool IsNarrativeDate { get; set; }

    /// <summary>
    /// Indica si el usuario hizo una confirmación explícita de reserva.
    /// </summary>
    public bool IsExplicitConfirmation { get; set; }

    /// <summary>
    /// Indica si se debe verificar disponibilidad en el backend.
    /// </summary>
    public bool ShouldCheckAvailability { get; set; }

    /// <summary>
    /// Indica si se debe permitir crear una reserva.
    /// Solo será true si se cumplen todas las condiciones necesarias.
    /// </summary>
    public bool ShouldAllowReservation { get; set; }

    /// <summary>
    /// Fecha detectada en formato raw (string) para logging/debugging.
    /// </summary>
    public string? DetectedDateRaw { get; set; }

    /// <summary>
    /// Hora detectada en formato raw (string) para logging/debugging.
    /// </summary>
    public string? DetectedTimeRaw { get; set; }
}
