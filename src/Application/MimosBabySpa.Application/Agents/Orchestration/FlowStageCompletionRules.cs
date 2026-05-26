using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Reglas únicas de completitud y transición de stages (multitenant, config-driven).
/// Usado por <see cref="FlowEngine"/> y <see cref="Composition.FlowStageDetector"/>.
/// </summary>
internal static class FlowStageCompletionRules
{
    /// <summary>
    /// El stage se considera terminado para navegación (saltar en FindApplicableStage).
    /// factsCollected: marcado en estado O todos los collects satisfechos.
    /// </summary>
    public static bool IsStageCompleted(
        AgentFlowStage stage,
        AgentToolContext session,
        FlowToolResult? lastToolResult = null)
    {
        var state = session.ConversationState;

        return stage.CompletedWhen switch
        {
            StageCompletionCriteria.Always =>
                state.CompletedOneShotStages.Contains(stage.Id),
            StageCompletionCriteria.FactsCollected =>
                state.CompletedOneShotStages.Contains(stage.Id)
                || AllCollectsSatisfied(stage, session, lastToolResult),
            StageCompletionCriteria.ToolSucceeded or StageCompletionCriteria.UserConfirms =>
                state.CompletedActionStages.Contains(stage.Id),
            _ => false
        };
    }

    /// <summary>
    /// Todos los entries de collects evalúan true (facts y markers result:).
    /// </summary>
    public static bool AllCollectsSatisfied(
        AgentFlowStage stage,
        AgentToolContext session,
        FlowToolResult? lastToolResult = null) =>
        stage.Collects.Count > 0
        && stage.Collects.All(k => EvalCollectEntry(k, session, lastToolResult));

    /// <summary>
    /// Al cerrar el turno: marca stages completados y devuelve si el stage actual
    /// pasó de incompleto a completo durante este turno (para transición en el mismo mensaje).
    /// </summary>
    public static bool ApplyEndOfTurn(
        AgentToolContext session,
        AgentFlowStage? stage,
        IReadOnlySet<string> completedStagesAtTurnStart,
        FlowTurnResult llmResult,
        FlowToolResult? lastToolResult = null,
        ILogger? logger = null)
    {
        if (stage is null)
            return false;

        var wasMarkedAtTurnStart = completedStagesAtTurnStart.Contains(stage.Id);

        switch (stage.CompletedWhen)
        {
            case StageCompletionCriteria.FactsCollected:
                if (AllCollectsSatisfied(stage, session, lastToolResult)
                    && !session.ConversationState.CompletedOneShotStages.Contains(stage.Id))
                {
                    MarkOneShotCompleted(session, stage);
                    logger?.LogInformation(
                        "Conv {C}: stage {Stage} completed by facts collection.",
                        session.ConversationId, stage.Id);
                }
                break;

            case StageCompletionCriteria.Always:
                MarkOneShotCompleted(session, stage);
                break;

            case StageCompletionCriteria.UserConfirms when llmResult.Intent == "Confirm":
                MarkActionCompleted(session, stage);
                break;
        }

        var completeNow = IsStageCompleted(stage, session, lastToolResult);
        return completeNow && !wasMarkedAtTurnStart;
    }

    public static bool EvalCollectEntry(
        string entry,
        AgentToolContext session,
        FlowToolResult? lastToolResult)
    {
        if (entry.StartsWith("result:", StringComparison.OrdinalIgnoreCase))
        {
            var spec = entry["result:".Length..];
            var eqIdx = spec.IndexOf('=');
            if (eqIdx < 0) return false;
            var field = spec[..eqIdx];
            var expected = spec[(eqIdx + 1)..];
            var actual = lastToolResult?.GetString(field)
                ?? lastToolResult?.GetBool(field)?.ToString().ToLowerInvariant();
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        return session.Facts.TryGetValue(entry, out var v) && !string.IsNullOrWhiteSpace(v);
    }

    public static HashSet<string> SnapshotCompletedOneShotStages(AgentToolContext session) =>
        new(session.ConversationState.CompletedOneShotStages, StringComparer.OrdinalIgnoreCase);

    public static void MarkOneShotCompleted(AgentToolContext session, AgentFlowStage stage) =>
        session.ConversationState.CompletedOneShotStages.Add(stage.Id);

    public static void MarkActionCompleted(AgentToolContext session, AgentFlowStage stage)
    {
        if (stage.CompletedWhen is StageCompletionCriteria.Always)
            session.ConversationState.CompletedOneShotStages.Add(stage.Id);
        else
            session.ConversationState.CompletedActionStages.Add(stage.Id);
    }
}
