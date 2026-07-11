using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed record StageTransitionDecision(
    bool ShouldTransition,
    string? TransitionId,
    string? TargetStageId,
    IReadOnlyList<StageEffectDefinition> Effects,
    string Reason)
{
    public static StageTransitionDecision Stay(string reason) => new(false, null, null, [], reason);
}

public sealed class DeterministicStageTransitionResolver
{
    private readonly StageConditionEvaluator _conditions;

    public DeterministicStageTransitionResolver(StageConditionEvaluator conditions) => _conditions = conditions;

    public StageTransitionDecision Resolve(
        AgentFlowDefinition flow,
        AgentFlowStage stage,
        DeterministicStageExecutionContext context)
    {
        var explicitTransition = stage.Transitions
            .Select((transition, index) => (transition, index))
            .OrderByDescending(value => value.transition.Priority)
            .ThenBy(value => value.index)
            .Select(value => value.transition)
            .FirstOrDefault(transition => _conditions.Evaluate(transition.Condition, context));

        if (explicitTransition is not null)
        {
            return new StageTransitionDecision(
                true,
                explicitTransition.Id,
                explicitTransition.To,
                explicitTransition.Effects,
                "explicit_condition_matched");
        }

        if (stage.Transitions.Count > 0)
            return StageTransitionDecision.Stay("no_explicit_condition_matched");

        if (stage.AdvanceWhenFacts.Count == 0)
            return StageTransitionDecision.Stay("no_advancement_rule");

        var ready = StageAdvanceFactReadiness.IsComplete(
            stage,
            context.Facts,
            context.OperationContext?.Config?.FactSchema ?? []);
        if (!ready)
            return StageTransitionDecision.Stay("advance_facts_missing");

        var index = IndexOf(flow.Stages, stage.Id);
        if (index < 0 || index + 1 >= flow.Stages.Count)
            return StageTransitionDecision.Stay("flow_complete");

        return new StageTransitionDecision(
            true,
            "advance_when_facts",
            flow.Stages[index + 1].Id,
            [],
            "advance_facts_complete");
    }

    private static int IndexOf(IReadOnlyList<AgentFlowStage> stages, string id)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            if (stages[index].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }
}
