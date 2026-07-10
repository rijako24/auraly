using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Gating;

public static class ToolFlowScope
{
    public static IReadOnlyList<IAgentTool> FilterVisibleTools(
        AgentConfig config,
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> effectiveTools,
        AgentToolContext? session = null)
    {
        if (!HasStageScope(currentStage))
            return effectiveTools;

        return effectiveTools
            .Where(tool => IsAllowedInScope(tool.Name, config, currentStage!, FlowRuntimeDecision.Empty, session))
            .ToList();
    }

    public static bool IsAllowedInScope(
        string toolName,
        AgentConfig config,
        AgentFlowStage? currentStage,
        FlowRuntimeDecision runtimeDecision,
        AgentToolContext? session = null) =>
        !HasStageScope(currentStage)
        || IsAllowedByStage(toolName, currentStage!)
        || IsAllowedByGlobalAction(toolName, config, runtimeDecision, session);

    private static bool HasStageScope(AgentFlowStage? currentStage) =>
        currentStage is not null && currentStage.AllowedActions.Count > 0;

    private static bool IsAllowedByStage(string toolName, AgentFlowStage currentStage) =>
        currentStage.AllowedActions.Contains(toolName, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedByGlobalAction(string toolName, AgentConfig config) =>
        config.GlobalActions.Any(action =>
            action.AllowedActions.Contains(toolName, StringComparer.OrdinalIgnoreCase));

    public static bool IsAllowedByGlobalAction(
        string toolName,
        AgentConfig config,
        FlowRuntimeDecision runtimeDecision,
        AgentToolContext? session = null) =>
        config.GlobalActions.Any(action =>
            AgentTurnToolScope.ShouldExposeGlobalActionToLlm(action, session)
            && action.AllowedActions.Contains(toolName, StringComparer.OrdinalIgnoreCase));
}