namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Snapshot de la sesión transaccional anterior, capturado antes de resetear el estado.
/// Permite al LLM referenciar preferencias previas (ej: add-ons) como sugerencias personalizadas
/// sin que contaminen el ciclo actual. Multi-tenant: TransactionalAttributes es genérico.
/// </summary>
public class PreviousSessionSnapshot
{
    /// <summary>
    /// Servicio elegido en la sesión anterior.
    /// </summary>
    public string? Service { get; set; }

    /// <summary>
    /// Fecha deseada en la sesión anterior.
    /// </summary>
    public DateOnly? Date { get; set; }

    /// <summary>
    /// Hora deseada en la sesión anterior.
    /// </summary>
    public TimeOnly? Time { get; set; }

    /// <summary>
    /// ID de reserva si la sesión anterior completó una reserva.
    /// </summary>
    public Guid? ReservationId { get; set; }

    /// <summary>
    /// Si la sesión anterior terminó con una reserva creada exitosamente.
    /// </summary>
    public bool WasCompleted { get; set; }

    /// <summary>
    /// Atributos transaccionales de la sesión anterior (ej: SelectedAddOns).
    /// Claves definidas por el negocio; el código no accede a claves específicas.
    /// </summary>
    public Dictionary<string, string> TransactionalAttributes { get; set; } = new();

    /// <summary>
    /// Momento en que se capturó el snapshot.
    /// </summary>
    public DateTime CapturedAt { get; set; }
}
