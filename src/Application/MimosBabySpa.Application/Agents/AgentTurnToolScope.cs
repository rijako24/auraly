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
            foreach (var toolName in currentStage.AllowedActions.Where(name => !string.IsNullOrWhiteSpace(name)))
                allowedNames.Add(toolName.Trim());
        }

        foreach (var action in OrderedGlobalActions(config))
        {
            if (action.AllowedActions.Count > 0)
                hasConfiguredScope = true;

            if (!ShouldExposeGlobalActionToLlm(action, session))
                continue;

            AddGlobalActionTools(action, effectiveTools, session, runtimeActive, allowedNames);
        }

        if (session.RuntimeDecision.ExtraAllowedToolNames.Count > 0)
            hasConfiguredScope = true;

        foreach (var toolName in session.RuntimeDecision.ExtraAllowedToolNames)
            allowedNames.Add(toolName);

        if (allowedNames.Count == 0)
            return hasConfiguredScope ? [] : effectiveTools;

        var byName = effectiveTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        return allowedNames
            .Select(name => byName.TryGetValue(name, out var tool) ? tool : null)
            .Where(tool => tool is not null)
            .Cast<IAgentTool>()
            .Where(tool => !runtimeActive || session.RuntimeDecision.IsToolAllowedByRuntime(tool))
            .ToList();
    }

    private static void AddGlobalActionTools(
        AgentGlobalAction action,
        IReadOnlyList<IAgentTool> effectiveTools,
        AgentToolContext session,
        bool runtimeActive,
        ISet<string> allowedNames)
    {
        var actionToolNames = action.AllowedActions
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (runtimeActive && IsDisabledByRuntime(actionToolNames, effectiveTools, session.RuntimeDecision))
            return;

        foreach (var toolName in actionToolNames)
            allowedNames.Add(toolName);
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

    public static bool ShouldExposeGlobalActionToLlm(AgentGlobalAction action, AgentToolContext? session)
    {
        if (action.EntryActions.Count == 0)
            return false;

        if (session is null)
            return false;

        return action.EntryActions.Any(entryAction => StageEntryActionMatcher.Matches(entryAction, session));
    }

    public static IReadOnlyList<AgentGlobalAction> OrderedGlobalActions(AgentConfig config) =>
        config.GlobalActions
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
