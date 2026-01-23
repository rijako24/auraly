namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Estados explícitos de una conversación para flujo determinístico.
/// Evita saltos de flujo y confirmaciones prematuras.
/// </summary>
public enum ConversationState
{
    /// <summary>
    /// Estado inicial, sin acción específica
    /// </summary>
    Idle = 0,
    
    /// <summary>
    /// Recolectando información del cliente (nombre, teléfono, edad del bebé, servicio deseado)
    /// </summary>
    CollectingData = 1,
    
    /// <summary>
    /// Verificando disponibilidad en el backend (NO el modelo decide)
    /// </summary>
    CheckingAvailability = 2,
    
    /// <summary>
    /// Datos completos y disponibilidad confirmada, listo para crear reserva
    /// </summary>
    ReadyToReserve = 3,
    
    /// <summary>
    /// Creando reserva en el backend (transacción atómica)
    /// </summary>
    CreatingReservation = 4,
    
    /// <summary>
    /// Reserva creada pero esperando confirmación de pago
    /// </summary>
    WaitingForPayment = 5,
    
    /// <summary>
    /// Reserva confirmada y pagada
    /// </summary>
    Confirmed = 6
}
