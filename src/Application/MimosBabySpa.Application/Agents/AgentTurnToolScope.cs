using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents;

internal static class AgentTurnToolScope
{
    public static IReadOnlyList<IAgentTool> Resolve(
        AgentConfig config,
        AgentToolContext session,
        IReadOnlyList<IAgentTool> effectiveTools,
        AgentFlowStage? currentStage)
    {
        var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runtimeActive = !ReferenceEquals(session.RuntimeDecision, Runtime.FlowRuntimeDecision.Empty);
        var hasConfiguredScope = false;

        if (currentStage?.AllowedActions.Count > 0)
        {
            hasConfiguredScope = true;
            foreach (var toolName in SemanticFlowActionResolver.ResolveToolNames(config, currentStage.AllowedActions))
                allowedNames.Add(toolName);
        }

        foreach (var action in OrderedGlobalActions(config))
        {
            if (runtimeActive && !session.RuntimeDecision.EnabledGlobalActionIds.Contains(action.Id))
                continue;

            if (action.AllowedActions.Count > 0)
                hasConfiguredScope = true;

            var actionToolNames = SemanticFlowActionResolver.ResolveToolNames(config, action.AllowedActions);
            if (runtimeActive && IsDisabledByRuntime(actionToolNames, effectiveTools, session.RuntimeDecision))
                continue;

            foreach (var toolName in actionToolNames)
                allowedNames.Add(toolName);
        }

        if (session.RuntimeDecision.ExtraAllowedToolNames.Count > 0)
            hasConfiguredScope = true;

        foreach (var toolName in session.RuntimeDecision.ExtraAllowedToolNames)
            allowedNames.Add(toolName);

        if (allowedNames.Count == 0)
            return hasConfiguredScope ? [] : effectiveTools;

        var byName = effectiveTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var scopedTools = allowedNames
            .Select(name => byName.TryGetValue(name, out var tool) ? tool : null)
            .Where(tool => tool is not null)
            .Cast<IAgentTool>()
            .Where(tool => !runtimeActive || session.RuntimeDecision.IsToolAllowedByRuntime(tool))
            .ToList();

        return scopedTools;
    }

    private static bool IsDisabledByRuntime(
        IReadOnlyList<string> toolNames,
        IReadOnlyList<IAgentTool> effectiveTools,
        Runtime.FlowRuntimeDecision decision)
    {
        if (decision.DisabledToolCapabilities.Count == 0)
            return false;

        var names = toolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return effectiveTools
            .Where(tool => names.Contains(tool.Name))
            .Any(tool => tool.Capabilities.Any(decision.DisabledToolCapabilities.Contains));
    }

    public static IReadOnlyList<AgentGlobalAction> OrderedGlobalActions(AgentConfig config) =>
        config.GlobalActions
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
