namespace MimosBabySpa.Application.Agents.Configuration;

public static class SemanticFlowActionResolver
{
    public static IReadOnlyList<string> ResolveToolNames(
        AgentConfig config,
        IReadOnlyList<string> actionIds)
    {
        if (actionIds.Count == 0)
            return [];

        var tools = new List<string>();
        foreach (var actionId in actionIds)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                continue;

            if (!config.FlowLanguage.Actions.TryGetValue(actionId.Trim(), out var action))
                continue;

            if (!string.IsNullOrWhiteSpace(action.Tool))
                tools.Add(action.Tool.Trim());
        }

        return tools;
    }
}
