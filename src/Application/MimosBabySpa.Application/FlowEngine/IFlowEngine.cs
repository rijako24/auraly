using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Motor determinístico de flujo transaccional.
/// Trabaja SOLO con ConversationState y RequiredFieldsConfiguration — nunca con texto libre.
///
/// Responsabilidades:
/// 1. Determinar qué datos faltan.
/// 2. Decidir qué acciones son posibles (CanCheckAvailability, CanCreateReservation).
/// 3. Determinar la etapa actual del flujo.
///
/// NUNCA: analiza texto, llama a BD, ni usa LLM.
/// </summary>
public interface IFlowEngine
{
    /// <summary>
    /// Evaluación completa del flujo. Actualiza state.CurrentStage como efecto secundario.
    /// </summary>
    FlowEvaluationResult Evaluate(ConversationState state, RequiredFieldsConfiguration requiredFields);

    /// <summary>
    /// True si se puede verificar disponibilidad POR PRIMERA VEZ o tras un reset.
    /// Retorna false si AvailabilityConfirmed ya es true (ya se verificó para los datos actuales).
    /// </summary>
    bool CanCheckAvailability(ConversationState state);

    /// <summary>
    /// True si hay datos suficientes (Service + Date) para llamar al backend de disponibilidad,
    /// independientemente de si ya se verificó. Usar para re-verificaciones explícitas.
    /// </summary>
    bool ShouldRecheckAvailability(ConversationState state);

    /// <summary>
    /// True si se pueden cumplir todos los requisitos para crear una reserva
    /// (incluyendo confirmación, disponibilidad, datos core y campos requeridos).
    /// </summary>
    bool CanCreateReservation(ConversationState state, RequiredFieldsConfiguration requiredFields);

    /// <summary>
    /// Retorna los campos aún faltantes para completar la transacción.
    /// </summary>
    List<string> GetMissingFields(ConversationState state, RequiredFieldsConfiguration requiredFields);

    /// <summary>
    /// Determina la etapa transaccional basándose en el estado actual y los campos requeridos.
    /// ConfirmingBooking solo se alcanza cuando todos los campos están completos (invariante).
    /// </summary>
    TransactionStage DetermineNextStage(ConversationState state, RequiredFieldsConfiguration requiredFields);
}
