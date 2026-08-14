namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Reglas globales para calcular disponibilidad.
/// Los horarios se resuelven desde BusinessWorkingHours y EmployeeWorkingHours.
/// </summary>
public class AvailabilityParams
{
    /// <summary>
    /// Intervalo en minutos entre slots candidatos al generar disponibilidad del dia.
    /// </summary>
    public int SlotIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Minutos de buffer tras cada cita, sumados a la duracion del servicio.
    /// </summary>
    public int BufferBetweenAppointmentsMinutes { get; set; } = 0;

    /// <summary>
    /// Anticipacion minima, en minutos, requerida para crear una reserva desde la hora actual del negocio.
    /// </summary>
    public int MinimumLeadTimeMinutes { get; set; } = 0;

    /// <summary>
    /// Si true, requiere empleado disponible para confirmar el slot.
    /// </summary>
    public bool RequireEmployee { get; set; } = true;

    /// <summary>
    /// Estrategia de asignacion de empleado.
    /// Valores: "least_versatile" (default), "round_robin", "most_available".
    /// </summary>
    public string EmployeeStrategy { get; set; } = "least_versatile";

    public static readonly AvailabilityParams Default = new();
}
