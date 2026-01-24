namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Estado completo de una conversación almacenado como JSON.
/// Diseñado para ser 100% genérico, multi-negocio y extensible dinámicamente.
/// </summary>
public class ConversationState
{
    // ============================================
    // IDENTIDAD DEL CLIENTE
    // ============================================
    
    /// <summary>
    /// Nombre del cliente.
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// Teléfono del cliente.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Email del cliente.
    /// </summary>
    public string? Email { get; set; }

    // ============================================
    // INTENCIONES
    // ============================================
    
    /// <summary>
    /// Intención actual detectada por el sistema.
    /// </summary>
    public IntentType? CurrentIntent { get; set; }

    /// <summary>
    /// Última intención detectada (para historial).
    /// </summary>
    public IntentType? LastIntent { get; set; }

    // ============================================
    // ENTIDADES DE NEGOCIO SELECCIONADAS
    // ============================================
    
    /// <summary>
    /// Entidad principal seleccionada (ej: servicio, producto, plan).
    /// Genérico: puede ser cualquier entidad del negocio.
    /// </summary>
    public string? PrimaryEntity { get; set; }

    /// <summary>
    /// Entidad secundaria seleccionada (ej: variante, opción adicional).
    /// Genérico: puede ser cualquier entidad del negocio.
    /// </summary>
    public string? SecondaryEntity { get; set; }

    // ============================================
    // ATRIBUTOS DINÁMICOS POR NEGOCIO
    // ============================================
    
    /// <summary>
    /// Atributos dinámicos específicos del negocio.
    /// Ejemplos:
    /// - babyAgeMonths (para spa de bebés)
    /// - partySize (para restaurantes)
    /// - vehicleType (para talleres)
    /// - roomType (para hoteles)
    /// Permite extensibilidad sin modificar el modelo base.
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    // ============================================
    // PROGRAMACIÓN (SCHEDULING)
    // ============================================
    
    /// <summary>
    /// Fecha deseada para la reserva/cita.
    /// </summary>
    public DateOnly? DesiredDate { get; set; }

    /// <summary>
    /// Hora deseada para la reserva/cita.
    /// </summary>
    public TimeOnly? DesiredTime { get; set; }

    /// <summary>
    /// Duración en minutos del servicio/cita.
    /// </summary>
    public int? DurationMinutes { get; set; }

    // ============================================
    // DISPONIBILIDAD
    // ============================================
    
    /// <summary>
    /// Indica si se ha verificado disponibilidad al menos una vez.
    /// </summary>
    public bool AvailabilityChecked { get; set; }

    /// <summary>
    /// Resultado de la última verificación de disponibilidad.
    /// null = no se ha verificado aún.
    /// true = disponible.
    /// false = no disponible.
    /// </summary>
    public bool? LastAvailabilityResult { get; set; }

    /// <summary>
    /// Fecha y hora de la última verificación de disponibilidad.
    /// </summary>
    public DateTime? LastAvailabilityCheckAt { get; set; }

    // ============================================
    // TRANSACCIÓN (RESERVA)
    // ============================================
    
    /// <summary>
    /// Indica si la reserva fue confirmada explícitamente por el cliente.
    /// </summary>
    public bool ReservationConfirmed { get; set; }

    /// <summary>
    /// ID de la reserva creada (si existe).
    /// </summary>
    public string? ReservationId { get; set; }

    // ============================================
    // CONTROL DE VERSIONES
    // ============================================
    
    /// <summary>
    /// Última actualización del estado.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Versión del estado (incrementa en cada modificación).
    /// Útil para optimistic concurrency y auditoría.
    /// </summary>
    public int Version { get; set; } = 1;
}
