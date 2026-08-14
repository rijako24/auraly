using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents.Composition;

internal sealed record FlowCheckpointInvalidationResult(
    IReadOnlyList<string> FactsToClear,
    IReadOnlyList<string> StageSnapshotsToReset);

internal static class FlowCheckpointInvalidation
{
    public static List<string> GetDerivedAdvanceFactsToClear(
        AgentConversationContext ctx,
        IReadOnlyCollection<string> changedFactKeys) =>
        GetInvalidations(ctx.Config, changedFactKeys).FactsToClear.ToList();

    public static FlowCheckpointInvalidationResult GetInvalidations(
        AgentConversationContext ctx,
        IReadOnlyCollection<string> changedFactKeys) =>
        GetInvalidations(ctx.Config, changedFactKeys);

    public static FlowCheckpointInvalidationResult GetInvalidations(
        AgentConfig? config,
        IReadOnlyCollection<string> changedFactKeys)
    {
        var changedKeys = NormalizeKeys(changedFactKeys);
        if (changedKeys.Count == 0)
            return new FlowCheckpointInvalidationResult([], []);

        var factSchema = config?.FactSchema ?? [];
        var dependencyClears = ResolveDependencyClears(factSchema, changedKeys);
        var initialSignals = UnionKeys(changedKeys, dependencyClears);

        var derivedFacts = factSchema
            .Where(entry =>
                (entry.Source.Equals("system", StringComparison.OrdinalIgnoreCase)
                    || entry.Source.Equals("session", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(entry.DefaultValue))
            .Select(entry => entry.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var initiallyAffectedStages = (config?.Flows.SelectMany(flow => flow.Stages) ?? [])
            .Where(stage => stage.ReentryOnFactChanged.Any(initialSignals.Contains))
            .ToList();

        var derivedAdvanceClears = initiallyAffectedStages
            .SelectMany(stage => stage.AdvanceWhenFacts)
            .Where(factKey => derivedFacts.Contains(factKey)
                && !changedKeys.Contains(factKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var factsToClear = dependencyClears
            .Concat(derivedAdvanceClears)
            .Where(factKey => !changedKeys.Contains(factKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allSignals = UnionKeys(changedKeys, factsToClear);
        var stageSnapshotsToReset = (config?.Flows.SelectMany(flow => flow.Stages) ?? [])
            .Where(stage => stage.ReentryOnFactChanged.Any(allSignals.Contains))
            .Select(stage => stage.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FlowCheckpointInvalidationResult(factsToClear, stageSnapshotsToReset);
    }

    private static HashSet<string> ResolveDependencyClears(
        IReadOnlyList<FactSchemaEntry> factSchema,
        HashSet<string> changedKeys)
    {
        var factsToClear = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(changedKeys);

        while (queue.Count > 0)
        {
            var changedKey = queue.Dequeue();
            foreach (var entry in factSchema)
            {
                if (!CanClearOnDependencyChange(entry)
                    || changedKeys.Contains(entry.Key)
                    || factsToClear.Contains(entry.Key)
                    || !DependsOn(entry, changedKey))
                {
                    continue;
                }

                factsToClear.Add(entry.Key);
                queue.Enqueue(entry.Key);
            }
        }

        return factsToClear;
    }

    private static bool CanClearOnDependencyChange(FactSchemaEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Key)
        && !entry.IsCustomerScoped()
        && entry.DependsOn is { Count: > 0 };

    private static bool DependsOn(FactSchemaEntry entry, string changedKey) =>
        entry.DependsOn.Any(dependency =>
            !string.IsNullOrWhiteSpace(dependency)
            && dependency.Trim().Equals(changedKey, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> NormalizeKeys(IEnumerable<string> keys) =>
        keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> UnionKeys(
        IEnumerable<string> left,
        IEnumerable<string> right) =>
        left.Concat(right).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
