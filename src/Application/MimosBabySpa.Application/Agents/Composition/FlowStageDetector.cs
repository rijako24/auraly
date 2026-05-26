using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Orchestration;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed class FlowStageDetector : IFlowStageDetector
{
    /// <summary>
    /// Detecta el stage activo recorriendo la lista en orden y saltando los completados.
    /// La evaluación de appliesWhen con @pack.X se delega al FlowEngine (que tiene el runtime context).
    /// Aquí solo se evalúan condiciones basadas en facts del ConversationState.
    /// </summary>
    public AgentFlowStage? DetectCurrentStage(AgentFlowDefinition flow, AgentToolContext? session)
    {
        if (flow.Stages.Count == 0) return null;

        foreach (var stage in flow.Stages)
        {
            if (session is not null
                && FlowStageCompletionRules.IsStageCompleted(stage, session))
                continue;

            // Condición básica sobre facts (sin @result.X ni @pack.X)
            if (stage.AppliesWhen is not null
                && !EvaluateBasicCondition(stage.AppliesWhen, session))
                continue;

            return stage;
        }

        return null;
    }

    private static bool EvaluateBasicCondition(
        AgentFlowStageCondition condition,
        AgentToolContext? session)
    {
        if (session is null) return false;

        if (condition.Field.StartsWith("@fact.", StringComparison.OrdinalIgnoreCase))
        {
            var key = condition.Field["@fact.".Length..];
            var actual = session.Facts.TryGetValue(key, out var v) ? v : null;
            return string.Equals(actual, condition.EqualsValue, StringComparison.OrdinalIgnoreCase);
        }

        // Para @pack.X y @result.X, el FlowEngine evalúa en tiempo de ejecución
        // El detector los acepta como "no filtrados" para no bloquear el flujo
        return true;
    }
}
