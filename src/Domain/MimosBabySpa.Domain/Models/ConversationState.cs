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
    /// TRUE cuando el formulario de confirmación ya fue presentado al cliente (una vez por ciclo de datos).
    /// Se resetea si cambian Service, DesiredDate, DesiredTime, datos de identidad o atributos del resumen.
    /// Permite inyectar el resumen determinísticamente la primera vez y no bombardear en turnos subsiguientes.
    /// </summary>
    public bool ConfirmationSummaryPresented { get; set; }

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
    // PAGO (solo el backend puede modificar estos campos)
    // ========================================

    /// <summary>
    /// Referencia única del link de pago generado (Wompi payment_link.id).
    /// Permite correlacionar webhook → conversación.
    /// Null si no se ha generado link.
    /// </summary>
    public string? PaymentReferenceId { get; set; }

    /// <summary>
    /// URL del link de pago para enviar al cliente.
    /// Solo se genera una vez por ciclo transaccional.
    /// </summary>
    public string? PaymentLinkUrl { get; set; }

    /// <summary>
    /// Monto del anticipo en centavos (para trazabilidad).
    /// </summary>
    public long? AnticipoAmountInCents { get; set; }

    /// <summary>
    /// Fecha de expiración del link de pago.
    /// </summary>
    public DateTime? PaymentLinkExpiresAt { get; set; }

    /// <summary>
    /// TRUE solo cuando el webhook de Wompi confirma el pago.
    /// El LLM y el usuario NUNCA pueden establecer esto en true.
    /// </summary>
    public bool PaymentConfirmed { get; set; }

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
    /// Inicio de la sesión transaccional actual. Se actualiza al resetear el estado para un nuevo ciclo.
    /// Usado para filtrar el historial de mensajes que ve el LLM.
    /// </summary>
    public DateTime SessionStartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Snapshot de la sesión anterior (preferencias transaccionales). Null si no hay sesión previa.
    /// Permite sugerencias personalizadas (ej: "te había interesado X") sin contaminar el ciclo actual.
    /// </summary>
    public PreviousSessionSnapshot? PreviousSession { get; set; }

    /// <summary>
    /// Último mensaje del usuario (para contexto)
    /// </summary>
    public string? LastUserMessage { get; set; }

    /// <summary>
    /// Último mensaje del bot (para contexto)
    /// </summary>
    public string? LastBotMessage { get; set; }

    // ========================================
    // SALUD DEL TURNO Y ESCALADO (HANDOVER)
    // ========================================

    /// <summary>
    /// Contador de turnos degradados consecutivos (extracción falló, respuesta LLM falló, excepción).
    /// Persistido en BD para soportar múltiples instancias. Se resetea en turno exitoso.
    /// </summary>
    public int ConsecutiveDegradedTurns { get; set; }

    /// <summary>
    /// Quién atiende actualmente la conversación. Bot procesa normalmente; Human inhibe el bot.
    /// La transición Human→Bot es explícita vía endpoint de release (link en notificación).
    /// </summary>
    public ConversationOwner Owner { get; set; } = ConversationOwner.Bot;

    /// <summary>
    /// Timestamp de la última escalación (auditoría). El release es explícito, no por cooldown.
    /// </summary>
    public DateTime? LastEscalatedAt { get; set; }

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
            ConfirmationSummaryPresented = ConfirmationSummaryPresented,
            ReservationCreated = ReservationCreated,
            ReservationId = ReservationId,
            PaymentReferenceId = PaymentReferenceId,
            PaymentLinkUrl = PaymentLinkUrl,
            AnticipoAmountInCents = AnticipoAmountInCents,
            PaymentLinkExpiresAt = PaymentLinkExpiresAt,
            PaymentConfirmed = PaymentConfirmed,
            CurrentIntent = CurrentIntent,
            LastIntent = LastIntent,
            CurrentStage = CurrentStage,
            Attributes = new Dictionary<string, string>(Attributes),
            Version = Version,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            SessionStartedAt = SessionStartedAt,
            PreviousSession = PreviousSession,
            LastUserMessage = LastUserMessage,
            LastBotMessage = LastBotMessage,
            ConsecutiveDegradedTurns = ConsecutiveDegradedTurns,
            Owner = Owner,
            LastEscalatedAt = LastEscalatedAt
        };
    }
}

/// <summary>
/// Indica quién atiende la conversación. La transición Human→Bot requiere release explícito.
/// </summary>
public enum ConversationOwner
{
    Bot,
    Human
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
    /// Esperando confirmación de pago — link enviado, pendiente webhook
    /// </summary>
    AwaitingPayment = 7,

    /// <summary>
    /// Conversación abandonada o fallida
    /// </summary>
    Failed = 99
}
