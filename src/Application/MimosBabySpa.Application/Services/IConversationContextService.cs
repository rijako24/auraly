using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio centralizado para gestionar el contexto de conversación.
/// Proporciona métodos tipados y genéricos para leer y escribir estado.
/// Elimina la necesidad de usar strings mágicos en el código.
/// </summary>
public interface IConversationContextService
{
    /// <summary>
    /// Obtiene el estado completo de la conversación.
    /// </summary>
    Task<ConversationState> GetAsync(Guid conversationId);

    // ============================================
    // IDENTIDAD
    // ============================================

    /// <summary>
    /// Establece la identidad del cliente.
    /// </summary>
    Task SetIdentityAsync(Guid conversationId, string? name = null, string? phone = null, string? email = null);

    // ============================================
    // INTENCIONES
    // ============================================

    /// <summary>
    /// Establece la intención actual detectada.
    /// </summary>
    Task SetIntentAsync(Guid conversationId, IntentType intent);

    // ============================================
    // ENTIDADES DE NEGOCIO
    // ============================================

    /// <summary>
    /// Establece la entidad principal seleccionada (ej: servicio, producto, plan).
    /// </summary>
    Task SetPrimaryEntityAsync(Guid conversationId, string entity);

    /// <summary>
    /// Establece la entidad secundaria seleccionada (ej: variante, opción adicional).
    /// </summary>
    Task SetSecondaryEntityAsync(Guid conversationId, string entity);

    // ============================================
    // ATRIBUTOS DINÁMICOS
    // ============================================

    /// <summary>
    /// Establece un atributo dinámico específico del negocio.
    /// Ejemplos: babyAgeMonths, partySize, vehicleType, roomType, etc.
    /// </summary>
    Task SetAttributeAsync(Guid conversationId, string key, string value);

    /// <summary>
    /// Elimina un atributo dinámico.
    /// </summary>
    Task RemoveAttributeAsync(Guid conversationId, string key);

    // ============================================
    // PROGRAMACIÓN (SCHEDULING)
    // ============================================

    /// <summary>
    /// Establece los parámetros de programación (fecha, hora, duración).
    /// </summary>
    Task SetScheduleAsync(Guid conversationId, DateOnly? date = null, TimeOnly? time = null, int? durationMinutes = null);

    // ============================================
    // DISPONIBILIDAD
    // ============================================

    /// <summary>
    /// Registra el resultado de una verificación de disponibilidad.
    /// </summary>
    Task SetAvailabilityAsync(Guid conversationId, bool result);

    /// <summary>
    /// Limpia el resultado de disponibilidad (útil cuando cambia fecha/hora).
    /// </summary>
    Task ClearAvailabilityAsync(Guid conversationId);

    // ============================================
    // TRANSACCIÓN (RESERVA)
    // ============================================

    /// <summary>
    /// Marca la reserva como confirmada y guarda el ID de la reserva creada.
    /// </summary>
    Task MarkReservationConfirmedAsync(Guid conversationId, string reservationId);

    // ============================================
    // CONTROL DE FLUJO
    // ============================================

    /// <summary>
    /// Resetea el flujo de conversación (limpia todo excepto identidad básica).
    /// Útil cuando el usuario quiere empezar de nuevo.
    /// </summary>
    Task ResetFlowAsync(Guid conversationId);

    // ============================================
    // MÉTODO GENÉRICO (PARA TOOLS)
    // ============================================

    /// <summary>
    /// Establece un campo del estado de forma genérica basándose en convenciones de nombres.
    /// Mapea automáticamente campos comunes a sus propiedades correspondientes.
    /// Si el campo no coincide con ninguna propiedad conocida, se guarda como atributo dinámico.
    /// </summary>
    /// <param name="conversationId">ID de la conversación</param>
    /// <param name="field">Nombre del campo (case-insensitive, acepta snake_case y camelCase)</param>
    /// <param name="value">Valor del campo como string</param>
    Task SetFieldAsync(Guid conversationId, string field, string value);
}
