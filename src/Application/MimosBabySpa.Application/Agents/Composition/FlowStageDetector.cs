using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed class FlowStageDetector : IFlowStageDetector
{
    public AgentFlowStage? DetectCurrentStage(AgentFlowDefinition flow, AgentToolContext? session)
    {
        if (flow.Stages.Count == 0)
            return null;

        if (!string.Equals(flow.StageDetection, "automatic", StringComparison.OrdinalIgnoreCase))
            return flow.Stages[0];

        foreach (var stage in flow.Stages)
        {
            if (IsStageComplete(stage, session)
                || IsStageSkipped(stage, session)
                || IsOneShotCompleted(stage, session)
                || !IsVariantApplicable(stage, session))
            {
                continue;
            }

            return stage;
        }

        return flow.Stages[^1];
    }

    /// <summary>
    /// Devuelve la variante activa de una etapa según el engagement del turno.
    /// Si la etapa no tiene variantes, devuelve null (se usan los valores base).
    /// </summary>
    public static AgentFlowStageVariant? GetActiveVariant(AgentFlowStage stage, AgentToolContext? session)
    {
        if (stage.Variants.Count == 0 || session is null)
            return null;

        var engagement = GetEngagementFact(session);
        if (string.IsNullOrWhiteSpace(engagement))
            return null;

        return stage.Variants.TryGetValue(engagement, out var variant) ? variant : null;
    }

    /// <summary>
    /// Una etapa con Variants solo aplica si el engagement actual está en el dict de variantes.
    /// Etapas sin Variants siempre aplican (behavior por defecto).
    /// </summary>
    public static bool IsVariantApplicable(AgentFlowStage stage, AgentToolContext? session)
    {
        if (stage.Variants.Count == 0)
            return true;

        if (session is null)
            return false;

        var engagement = GetEngagementFact(session);
        return !string.IsNullOrWhiteSpace(engagement)
               && stage.Variants.ContainsKey(engagement);
    }

    /// <summary>
    /// Una etapa CompletesOnEnter se omite si ya figura en CompletedOneShotStages del estado.
    /// </summary>
    public static bool IsOneShotCompleted(AgentFlowStage stage, AgentToolContext? session)
    {
        if (!stage.CompletesOnEnter)
            return false;

        return session?.ConversationState?.CompletedOneShotStages.Contains(stage.Id) ?? false;
    }

    /// <summary>
    /// Una etapa se considera saltable si SkipWhen está definido y TODOS sus facts están presentes.
    /// Cuando se salta, los AutoSetOnSkip ya fueron aplicados en LoadTurnSessionAsync.
    /// </summary>
    public static bool IsStageSkipped(AgentFlowStage stage, AgentToolContext? session)
    {
        if (string.IsNullOrWhiteSpace(stage.SkipWhen) || session is null)
            return false;

        var conditions = stage.SkipWhen.Split(
            "&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return conditions.Length > 0 && conditions.All(cond => HasFact(session, cond));
    }

    private static bool IsStageComplete(AgentFlowStage stage, AgentToolContext? session)
    {
        // CompletesOnEnter no avanza por facts sino por IsOneShotCompleted
        if (stage.CompletesOnEnter)
            return false;

        // Sin facts requeridos → la etapa permanece activa hasta salir del flujo (etapa terminal/acción)
        if (stage.AdvanceWhenFacts.Count == 0)
            return false;

        foreach (var factKey in stage.AdvanceWhenFacts)
        {
            if (!HasFact(session, factKey))
                return false;
        }

        return true;
    }

    private static bool HasFact(AgentToolContext? session, string factKey)
    {
        if (session is null)
            return false;

        return session.Facts.TryGetValue(factKey, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetEngagementFact(AgentToolContext session)
    {
        if (session.Facts.TryGetValue("session.engagement", out var val)
            && !string.IsNullOrWhiteSpace(val))
        {
            return val.Trim();
        }

        return null;
    }
}
