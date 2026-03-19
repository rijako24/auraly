namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Parámetros de disponibilidad leídos del config del nodo <c>check_availability</c>.
///
/// Configuración completa para calcular slots disponibles sin leer ninguna tabla
/// BusinessConfigurations. Cada nodo puede tener su propia política de agendamiento.
///
/// Estructura esperada en el config del nodo (propiedad "scheduling"):
/// <code>
/// "scheduling": {
///   "slotIntervalMinutes": 60,
///   "bufferBetweenAppointmentsMinutes": 0,
///   "requireEmployee": true,
///   "employeeStrategy": "least_versatile",
///   "schedule": {
///     "monday":    [{ "open": "08:00", "close": "18:00" }],
///     "tuesday":   [{ "open": "08:00", "close": "18:00" }],
///     "wednesday": [{ "open": "08:00", "close": "18:00" }],
///     "thursday":  [{ "open": "08:00", "close": "18:00" }],
///     "friday":    [{ "open": "08:00", "close": "18:00" }],
///     "saturday":  [{ "open": "09:00", "close": "17:00" }]
///   }
/// }
/// </code>
/// </summary>
public class AvailabilityParams
{
    /// <summary>
    /// Intervalo en minutos entre slots candidatos al generar disponibilidad del día.
    /// Ejemplo: 60 → slots en 08:00, 09:00, 10:00...
    /// </summary>
    public int SlotIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Minutos de buffer/limpieza tras cada cita, sumados a la duración del servicio
    /// para calcular el fin efectivo del slot (evita solapamientos).
    /// </summary>
    public int BufferBetweenAppointmentsMinutes { get; set; } = 0;

    /// <summary>
    /// Si true, requiere empleado disponible para confirmar el slot.
    /// Si false, omite la validación de empleado (negocios sin empleados asignados).
    /// </summary>
    public bool RequireEmployee { get; set; } = true;

    /// <summary>
    /// Estrategia de asignación de empleado.
    /// Valores: "least_versatile" (default), "round_robin", "most_available".
    /// </summary>
    public string EmployeeStrategy { get; set; } = "least_versatile";

    /// <summary>
    /// Horarios de operación por día de la semana.
    /// Clave: nombre del día en inglés en minúscula ("monday", "tuesday", ...).
    /// Valor: lista de bloques horarios (puede ser más de uno por día).
    /// Null → sin restricción de horario (no se generan candidatos).
    /// </summary>
    public Dictionary<string, List<TimeBlock>>? Schedule { get; set; }

    public static readonly AvailabilityParams Default = new();
}
