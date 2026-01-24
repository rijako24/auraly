using MimosBabySpa.Application.Models;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para detectar la intención del usuario y controlar el flujo de la conversación.
/// Implementa reglas determinísticas primero, luego heurísticas con estado, y finalmente fallback a IA solo para clasificación.
/// </summary>
public interface IIntentDetectorService
{
    /// <summary>
    /// Detecta la intención del usuario y determina las acciones permitidas.
    /// </summary>
    /// <param name="userMessage">Mensaje del usuario a analizar</param>
    /// <param name="state">Estado actual de la conversación</param>
    /// <returns>Resultado de la detección con todas las decisiones de control de flujo</returns>
    IntentDetectionResult Detect(string userMessage, ConversationState state);

    /// <summary>
    /// Evalúa si se permite crear una reserva basándose únicamente en el estado actual.
    /// Útil cuando se recalcula la intención después de actualizar el contexto.
    /// </summary>
    /// <param name="state">Estado actual de la conversación</param>
    /// <returns>Resultado de la evaluación con todas las decisiones de control de flujo</returns>
    IntentDetectionResult EvaluateFromState(ConversationState state);
}
