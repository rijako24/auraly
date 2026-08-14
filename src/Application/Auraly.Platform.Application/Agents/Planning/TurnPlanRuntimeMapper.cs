using Auraly.Platform.Application.Agents.Runtime;

namespace Auraly.Platform.Application.Agents.Planning;

public static class TurnPlanRuntimeMapper
{
    public static IReadOnlyList<SemanticSignal> ToSemanticSignals(TurnPlan plan) =>
        plan.Signals
            .Select(signal => new SemanticSignal(signal.Type, signal.Value.Clone(), signal.Evidence))
            .ToList();
}
