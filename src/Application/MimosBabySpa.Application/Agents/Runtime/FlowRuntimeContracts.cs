using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed record TurnEvent(string Type, string? Value = null);

public enum FlowRuntimeState
{
    Default
}

public sealed record FlowRouteDecision(
    string ActiveFlowId,
    string Decision,
    string Reason,
    double Confidence,
    bool IsPrimaryFlow)
{
    public static FlowRouteDecision Primary(string flowId, string reason = "primary_flow") =>
        new(flowId, "primary_flow", reason, 1.0, true);
}

public sealed record FlowRuntimeDecision(
    FlowRuntimeState State,
    IReadOnlyList<TurnEvent> Events,
    IReadOnlyDictionary<string, string> FactMutations,
    FlowRouteDecision Route,
    IReadOnlySet<string> ExtraAllowedToolNames,
    IReadOnlySet<string> BlockedToolNames,
    IReadOnlySet<string> DisabledToolCapabilities)
{
    public static FlowRuntimeDecision Empty { get; } = new(
        FlowRuntimeState.Default,
        [],
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        FlowRouteDecision.Primary(string.Empty),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool IsToolAllowedByRuntime(IAgentTool tool) =>
        !BlockedToolNames.Contains(tool.Name)
        && !tool.Capabilities.Any(DisabledToolCapabilities.Contains);

    public bool IsToolAllowedByRuntime(string toolName) =>
        !BlockedToolNames.Contains(toolName);
}

public interface ITurnEventExtractor
{
    IReadOnlyList<TurnEvent> Extract(string userMessage);
}

public interface IFlowRuntimeStateResolver
{
    FlowRuntimeState Resolve(AgentConfig config, AgentToolContext session);
}

public interface IFlowPolicyEngine
{
    FlowRuntimeDecision Decide(
        AgentConfig config,
        AgentToolContext session,
        FlowRuntimeState state,
        IReadOnlyList<TurnEvent> events,
        FlowRouteDecision route);
}

public interface IFlowRouter
{
    Task<FlowRouteDecision> RouteAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct);
}

public interface IFlowRuntimeOrchestrator
{
    Task<FlowRuntimeDecision> ApplyAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct);
}
