using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Flow Engine (Flow Brain) - Motor determinístico de flujo transaccional.
/// 
/// Este es el CEREBRO REAL del sistema. Trabaja SOLO con:
/// - ConversationState (datos estructurados)
/// - Campos requeridos (configuración)
/// - Flags de confirmación
/// 
/// NUNCA:
/// - Analiza texto del usuario
/// - Contiene lógica de negocio específica
/// - Inventa datos
/// - Toma decisiones sobre disponibilidad o recursos
/// 
/// Responsabilidades:
/// 1. Determinar qué datos faltan para completar una transacción
/// 2. Decidir qué herramientas (tools) pueden ejecutarse
/// 3. Validar si se puede avanzar a la siguiente etapa
/// 4. Determinar el estado del flujo transaccional
/// </summary>
public interface IFlowEngine
{
    /// <summary>
    /// Evalúa el estado actual del flujo y determina qué acciones son posibles.
    /// Esta es la función principal del FlowBrain.
    /// </summary>
    /// <param name="state">Estado actual de la conversación</param>
    /// <param name="requiredFields">Campos requeridos para este negocio</param>
    /// <returns>Resultado de la evaluación con acciones permitidas</returns>
    FlowEvaluationResult Evaluate(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields);

    /// <summary>
    /// Determina si se puede verificar disponibilidad en este momento.
    /// </summary>
    bool CanCheckAvailability(ConversationState state);

    /// <summary>
    /// Determina si se puede crear una reserva en este momento.
    /// Requiere confirmación explícita del usuario Y disponibilidad confirmada.
    /// </summary>
    bool CanCreateReservation(ConversationState state);

    /// <summary>
    /// Obtiene la lista de campos faltantes para completar la transacción.
    /// </summary>
    List<string> GetMissingFields(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields);

    /// <summary>
    /// Determina la siguiente etapa transaccional basándose en el estado.
    /// </summary>
    TransactionStage DetermineNextStage(ConversationState state);
}
