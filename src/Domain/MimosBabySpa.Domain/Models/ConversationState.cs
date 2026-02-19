using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Estado de conversación genérico y domain-agnostic.
/// Es la ÚNICA fuente de verdad sobre datos recolectados del usuario.
/// Este modelo es determinístico, serializable, auditable y replayable.
/// 
/// PRINCIPIOS ARQUITECTÓNICOS:
/// - Solo almacena valores estructurados (nunca frases o texto libre)
/// - No contiene lógica de negocio
/// - Los datos específicos de negocio van SIEMPRE en Attributes
/// - Es inmutable desde el punto de vista transaccional
/// - Versionado para auditoría
/// </summary>
public class ConversationState
{
    /// <summary>
    /// Identificador único del estado
    /// </summary>
    public Guid StateId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID del negocio al que pertenece esta conversación
    /// </summary>
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Número de teléfono del cliente
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    // ========================================
    // CAMPOS CORE (TRANSACCIONALES)
    // ========================================

    /// <summary>
    /// Nombre del cliente (valor estructurado, no frase)
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// Email del cliente (valor estructurado, validado)
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Servicio solicitado (DEBE ser nombre EXACTO del servicio del catálogo)
    /// </summary>
    public string? Service { get; set; }

    /// <summary>
    /// Fecha deseada para la reserva (ISO format)
    /// </summary>
    public DateOnly? DesiredDate { get; set; }

    /// <summary>
    /// Hora deseada para la reserva (ISO format)
    /// </summary>
    public TimeOnly? DesiredTime { get; set; }

    // ========================================
    // FLAGS DE CONFIRMACIÓN
    // ========================================

    /// <summary>
    /// TRUE solo si el backend confirmó que hay disponibilidad
    /// El LLM NUNCA puede establecer esto en true, solo el backend
    /// </summary>
    public bool AvailabilityConfirmed { get; set; }

    /// <summary>
    /// Horarios disponibles encontrados por el backend (formato CSV: "09:00,11:00,14:00,16:00")
    /// Solo se llena cuando se verifica disponibilidad para una fecha específica
    /// El LLM DEBE mostrar estos horarios al usuario cuando estén disponibles
    /// </summary>
    public string? AvailableTimeSlots { get; set; }

    /// <summary>
    /// TRUE solo si el usuario dio confirmación explícita de reserva
    /// Y todos los datos requeridos están completos
    /// </summary>
    public bool ReservationConfirmed { get; set; }

    /// <summary>
    /// TRUE cuando el bot ya ofreció add-ons al cliente (una vez por servicio elegido).
    /// Se resetea si el cliente cambia de servicio. Permite garantizar oferta 100% sin repetir.
    /// </summary>
    public bool AddOnsOffered { get; set; }

    /// <summary>
    /// TRUE solo si el backend creó la reserva exitosamente
    /// El LLM NUNCA puede establecer esto en true, solo el backend
    /// </summary>
    public bool ReservationCreated { get; set; }

    /// <summary>
    /// ID de la reserva creada (solo si ReservationCreated = true)
    /// </summary>
    public Guid? ReservationId { get; set; }

    // ========================================
    // CONTEXTO DE INTENCIÓN (FLOW STATE)
    // ========================================

    /// <summary>
    /// Intención actual detectada
    /// </summary>
    public IntentType CurrentIntent { get; set; }

    /// <summary>
    /// Intención anterior (para tracking de flujo)
    /// </summary>
    public IntentType? LastIntent { get; set; }

    /// <summary>
    /// Etapa actual del flujo transaccional
    /// </summary>
    public TransactionStage CurrentStage { get; set; }

    // ========================================
    // ATRIBUTOS DINÁMICOS (BUSINESS-SPECIFIC)
    // ========================================

    /// <summary>
    /// Diccionario de atributos específicos del negocio.
    /// Ejemplos:
    /// - Baby Spa: {"BabyAge": "6", "BabyName": "Lucas", "SpecialConditions": "Ninguna"}
    /// - Restaurant: {"PartySize": "4", "DietaryRestrictions": "Vegetarian"}
    /// - Medical: {"Symptoms": "Dolor de cabeza", "Insurance": "Blue Cross"}
    /// 
    /// REGLA: El código NUNCA debe acceder a claves específicas.
    /// Solo el prompt y el backend conocen qué campos existen.
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    // ========================================
    // METADATOS Y AUDITORÍA
    // ========================================

    /// <summary>
    /// Versión del estado (incrementa con cada actualización)
    /// Para optimistic locking y auditoría
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Timestamp de creación
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp de última actualización
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Último mensaje del usuario (para contexto)
    /// </summary>
    public string? LastUserMessage { get; set; }

    /// <summary>
    /// Último mensaje del bot (para contexto)
    /// </summary>
    public string? LastBotMessage { get; set; }

    // ========================================
    // MÉTODOS HELPER
    // ========================================

    /// <summary>
    /// Obtiene un atributo específico del negocio de forma segura
    /// </summary>
    public string? GetAttribute(string key)
    {
        return Attributes.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Establece un atributo específico del negocio
    /// </summary>
    public void SetAttribute(string key, string value)
    {
        Attributes[key] = value;
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }

    /// <summary>
    /// Verifica si un atributo existe y tiene valor
    /// </summary>
    public bool HasAttribute(string key)
    {
        return Attributes.ContainsKey(key) && !string.IsNullOrWhiteSpace(Attributes[key]);
    }

    /// <summary>
    /// Clona el estado para operaciones transaccionales
    /// </summary>
    public ConversationState Clone()
    {
        return new ConversationState
        {
            StateId = StateId,
            BusinessId = BusinessId,
            Phone = Phone,
            CustomerName = CustomerName,
            Email = Email,
            Service = Service,
            DesiredDate = DesiredDate,
            DesiredTime = DesiredTime,
            AvailabilityConfirmed = AvailabilityConfirmed,
            AvailableTimeSlots = AvailableTimeSlots,
            ReservationConfirmed = ReservationConfirmed,
            AddOnsOffered = AddOnsOffered,
            ReservationCreated = ReservationCreated,
            ReservationId = ReservationId,
            CurrentIntent = CurrentIntent,
            LastIntent = LastIntent,
            CurrentStage = CurrentStage,
            Attributes = new Dictionary<string, string>(Attributes),
            Version = Version,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastUserMessage = LastUserMessage,
            LastBotMessage = LastBotMessage
        };
    }
}

/// <summary>
/// Etapas del flujo transaccional genérico
/// (más simple que SalesStage, enfocado en la transacción)
/// </summary>
public enum TransactionStage
{
    /// <summary>
    /// Recolectando información básica del cliente
    /// </summary>
    CollectingInformation = 1,

    /// <summary>
    /// Explorando opciones de servicio
    /// </summary>
    ExploringServices = 2,

    /// <summary>
    /// Verificando disponibilidad
    /// </summary>
    CheckingAvailability = 3,

    /// <summary>
    /// Completando datos de identidad/negocio (disponibilidad ya confirmada, faltan campos)
    /// </summary>
    CompletingProfile = 4,

    /// <summary>
    /// Confirmando reserva — todos los datos completos, solo falta el "sí" del usuario
    /// </summary>
    ConfirmingBooking = 5,

    /// <summary>
    /// Reserva completada
    /// </summary>
    BookingCompleted = 6,

    /// <summary>
    /// Conversación abandonada o fallida
    /// </summary>
    Failed = 99
}
