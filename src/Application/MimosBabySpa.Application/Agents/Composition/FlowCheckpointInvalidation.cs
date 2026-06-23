namespace MimosBabySpa.Application.Agents.Composition;

internal static class FlowCheckpointInvalidation
{
    public static List<string> GetDerivedAdvanceFactsToClear(
        AgentToolContext ctx,
        IReadOnlyCollection<string> changedFactKeys)
    {
        if (changedFactKeys.Count == 0)
            return [];

        var changedKeys = changedFactKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derivedFacts = (ctx.Config?.FactSchema ?? [])
            .Where(entry => !entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (ctx.Config?.Flow.Stages ?? [])
            .Where(stage => stage.ReentryOnFactChanged.Any(changedKeys.Contains))
            .SelectMany(stage => stage.AdvanceWhenFacts)
            .Where(factKey => derivedFacts.Contains(factKey)
                && !changedKeys.Contains(factKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
