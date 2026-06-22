namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class FlowCheckpointInvalidation
{
    public static List<string> GetDerivedAdvanceFactsToClear(
        AgentToolContext ctx,
        IReadOnlyCollection<string> changedFactKeys)
    {
        if (changedFactKeys.Count == 0)
            return [];

        var derivedFacts = (ctx.Config?.FactSchema ?? [])
            .Where(entry => !entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (ctx.Config?.Flow.Stages ?? [])
            .Where(stage => stage.ReentryOnFactChanged.Any(changedFactKeys.Contains))
            .SelectMany(stage => stage.AdvanceWhenFacts)
            .Where(factKey => derivedFacts.Contains(factKey)
                && !changedFactKeys.Contains(factKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
