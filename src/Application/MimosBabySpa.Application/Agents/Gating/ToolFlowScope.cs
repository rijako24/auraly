using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Gating;

public static class ToolFlowScope
{
    public static IReadOnlyList<IAgentTool> FilterVisibleTools(
        AgentConfig config,
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> effectiveTools)
    {
        if (!HasStageScope(config, currentStage))
            return effectiveTools;

        return effectiveTools
            .Where(tool => IsAllowedInScope(tool.Name, config, currentStage!, FlowRuntimeDecision.Empty))
            .ToList();
    }

    public static bool IsAllowedInScope(
        string toolName,
        AgentConfig config,
        AgentFlowStage? currentStage,
        FlowRuntimeDecision runtimeDecision) =>
        !HasStageScope(config, currentStage)
        || IsAllowedByStage(toolName, config, currentStage!)
        || IsAllowedByGlobalAction(toolName, config, runtimeDecision);

    private static bool HasStageScope(AgentConfig config, AgentFlowStage? currentStage) =>
        config.Flow.Stages.Count > 0
        && currentStage is not null
        && currentStage.AllowedActions.Count > 0;

    private static bool IsAllowedByStage(string toolName, AgentConfig config, AgentFlowStage currentStage) =>
        SemanticFlowActionResolver.ResolveToolNames(config, currentStage.AllowedActions)
            .Contains(toolName, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedByGlobalAction(string toolName, AgentConfig config) =>
        IsAllowedByGlobalAction(toolName, config, FlowRuntimeDecision.Empty);

    public static bool IsAllowedByGlobalAction(string toolName, AgentConfig config, FlowRuntimeDecision runtimeDecision)
    {
        var runtimeActive = !ReferenceEquals(runtimeDecision, FlowRuntimeDecision.Empty);
        return config.GlobalActions.Any(action =>
            (!runtimeActive || runtimeDecision.EnabledGlobalActionIds.Contains(action.Id))
            && SemanticFlowActionResolver.ResolveToolNames(config, action.AllowedActions)
                .Contains(toolName, StringComparer.OrdinalIgnoreCase));
    }
}
