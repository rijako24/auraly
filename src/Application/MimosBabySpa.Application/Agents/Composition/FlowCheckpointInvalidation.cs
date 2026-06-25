namespace MimosBabySpa.Application.Agents.Composition;

internal sealed record FlowCheckpointInvalidationResult(
    IReadOnlyList<string> FactsToClear,
    IReadOnlyList<string> StageSnapshotsToReset);

internal static class FlowCheckpointInvalidation
{
    public static List<string> GetDerivedAdvanceFactsToClear(
        AgentToolContext ctx,
        IReadOnlyCollection<string> changedFactKeys) =>
        GetInvalidations(ctx, changedFactKeys).FactsToClear.ToList();

    public static FlowCheckpointInvalidationResult GetInvalidations(
        AgentToolContext ctx,
        IReadOnlyCollection<string> changedFactKeys)
    {
        if (changedFactKeys.Count == 0)
            return new FlowCheckpointInvalidationResult([], []);

        var changedKeys = changedFactKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derivedFacts = (ctx.Config?.FactSchema ?? [])
            .Where(entry => !entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedStages = (ctx.Config?.Flow.Stages ?? [])
            .Where(stage => stage.ReentryOnFactChanged.Any(changedKeys.Contains))
            .ToList();

        var factsToClear = affectedStages
            .SelectMany(stage => stage.AdvanceWhenFacts)
            .Where(factKey => derivedFacts.Contains(factKey)
                && !changedKeys.Contains(factKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stageSnapshotsToReset = affectedStages
            .Select(stage => stage.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FlowCheckpointInvalidationResult(factsToClear, stageSnapshotsToReset);
    }
}
