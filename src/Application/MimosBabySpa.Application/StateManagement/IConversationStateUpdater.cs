using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// ÚNICA fuente de verdad para aplicar cambios al ConversationState.
/// Centraliza todas las reglas de mutación: resets al cambiar servicio/fecha/hora,
/// validación de formatos, e incremento de Version.
///
/// Multitenant: el estado ya está asociado a conversationId/businessId por el llamador.
/// No persiste: el llamador decide cuándo guardar en BD.
/// </summary>
public interface IConversationStateUpdater
{
    /// <summary>
    /// Aplica un campo de datos al estado (CustomerName, Service, DesiredDate, etc. o Attribute:X).
    /// Aplica reglas de reset: cambiar Service/DesiredDate/DesiredTime invalida AvailabilityConfirmed y ReservationConfirmed.
    /// </summary>
    ApplyFieldResult ApplyField(ConversationState state, string field, string value);

    /// <summary>
    /// Aplica un flag de confirmación al estado (ReservationConfirmed, AvailabilityConfirmed, AvailableTimeSlots).
    /// Usar en lugar de mutación directa desde el orquestador o tools.
    /// </summary>
    ApplyFieldResult ApplyConfirmationFlag(ConversationState state, string flag, bool value, string? extraData = null);

    /// <summary>
    /// Resetea todos los flags transaccionales: AvailabilityConfirmed, ReservationConfirmed, AvailableTimeSlots.
    /// Usar cuando el usuario cancela o cambia de intención.
    /// </summary>
    void ResetTransactionalFlags(ConversationState state);
}

/// <summary>
/// Resultado de aplicar un campo o flag (sin excepción).
/// </summary>
public readonly record struct ApplyFieldResult(bool Success, string Message);
