namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class FlowPolicyEngine : IFlowPolicyEngine
{
    public FlowRuntimeDecision Decide(
        AgentConfig config,
        AgentToolContext session,
        FlowRuntimeState state,
        IReadOnlyList<TurnEvent> events,
        FlowRouteDecision route) =>
        new(
            state,
            events,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            route,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}