using MimosBabySpa.Application.Agents.Configuration;
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
            .Where(tool => IsAllowedInScope(tool.Name, config, currentStage!))
            .ToList();
    }

    public static bool IsAllowedInScope(string toolName, AgentConfig config, AgentFlowStage? currentStage) =>
        !HasStageScope(config, currentStage)
        || IsAllowedByStage(toolName, currentStage!)
        || IsAllowedByGlobalAction(toolName, config);

    private static bool HasStageScope(AgentConfig config, AgentFlowStage? currentStage) =>
        config.Flow.Stages.Count > 0
        && currentStage is not null
        && currentStage.AllowedTools.Count > 0;

    private static bool IsAllowedByStage(string toolName, AgentFlowStage currentStage) =>
        currentStage.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedByGlobalAction(string toolName, AgentConfig config) =>
        config.GlobalActions.Any(action =>
            action.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase));
}
