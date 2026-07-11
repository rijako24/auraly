using MimosBabySpa.Application.Agents.Runtime;

namespace MimosBabySpa.Application.Agents.Planning;

public static class TurnPlanRuntimeMapper
{
    public static IReadOnlyList<SemanticSignal> ToSemanticSignals(TurnPlan plan) =>
        plan.Signals
            .Select(signal => new SemanticSignal(signal.Type, signal.Value.Clone(), signal.Evidence))
            .ToList();
}
