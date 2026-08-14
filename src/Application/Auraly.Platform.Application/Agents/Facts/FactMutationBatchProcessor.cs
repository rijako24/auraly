using System.Text.Json;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Planning;

namespace Auraly.Platform.Application.Agents.Facts;

public sealed record FactMutationBatchResult(
    IReadOnlyDictionary<string, string> NextFacts,
    IReadOnlyDictionary<string, string?> Mutations,
    IReadOnlySet<string> ChangedFacts,
    IReadOnlySet<string> InvalidatedFacts,
    IReadOnlyDictionary<string, long> Versions);

public sealed class FactMutationBatchProcessor
{
    public FactMutationBatchResult Apply(
        IReadOnlyDictionary<string, FactSchemaEntry> schema,
        IReadOnlyList<PlannedFactClaim> claims,
        IReadOnlyDictionary<string, string> currentFacts,
        IReadOnlyDictionary<string, long>? currentVersions = null)
    {
        EnsureBatchIsStructurallySafe(schema, claims);
        var requested = claims.ToDictionary(
            claim => claim.Key,
            claim => claim.Operation.Equals(TurnPlanOperations.Clear, StringComparison.OrdinalIgnoreCase)
                ? null
                : CanonicalText(claim.Value),
            StringComparer.OrdinalIgnoreCase);
        return ApplyMutations(schema, requested, currentFacts, currentVersions);
    }

    public FactMutationBatchResult ApplyMutations(
        IReadOnlyDictionary<string, FactSchemaEntry> schema,
        IReadOnlyDictionary<string, string?> requestedMutations,
        IReadOnlyDictionary<string, string> currentFacts,
        IReadOnlyDictionary<string, long>? currentVersions = null)
    {
        foreach (var key in requestedMutations.Keys)
        {
            if (!schema.ContainsKey(key))
                throw new InvalidOperationException($"Fact '{key}' is not present in the compiled schema.");
        }

        var next = new Dictionary<string, string>(currentFacts, StringComparer.OrdinalIgnoreCase);
        var mutations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, long>(currentVersions ?? new Dictionary<string, long>(), StringComparer.OrdinalIgnoreCase);
        var explicitlyMutated = requestedMutations.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, nextValue) in requestedMutations)
        {
            next.TryGetValue(key, out var previous);
            if (string.Equals(previous, nextValue, StringComparison.Ordinal))
                continue;

            ApplyMutation(next, mutations, key, nextValue);
            MarkChanged(key, changed, versions);
        }

        InvalidateDependents(schema, next, mutations, changed, invalidated, versions, explicitlyMutated);

        return new FactMutationBatchResult(next, mutations, changed, invalidated, versions);
    }

    private static void EnsureBatchIsStructurallySafe(
        IReadOnlyDictionary<string, FactSchemaEntry> schema,
        IReadOnlyList<PlannedFactClaim> claims)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims)
        {
            if (!seen.Add(claim.Key))
                throw new InvalidOperationException($"Fact '{claim.Key}' appears more than once in an atomic batch.");
            if (!schema.ContainsKey(claim.Key))
                throw new InvalidOperationException($"Fact '{claim.Key}' is not present in the compiled schema.");
            if (claim.Operation is not (TurnPlanOperations.Set or TurnPlanOperations.Clear))
                throw new InvalidOperationException($"Fact operation '{claim.Operation}' is not supported.");
        }
    }

    private static void InvalidateDependents(
        IReadOnlyDictionary<string, FactSchemaEntry> schema,
        IDictionary<string, string> next,
        IDictionary<string, string?> mutations,
        ISet<string> changed,
        ISet<string> invalidated,
        IDictionary<string, long> versions,
        IReadOnlySet<string> explicitlyMutated)
    {
        var pending = new Queue<string>(changed);
        while (pending.TryDequeue(out var dependency))
        {
            foreach (var definition in schema.Values.Where(definition =>
                         !definition.IsCustomerScoped()
                         && definition.DependsOn.Contains(dependency, StringComparer.OrdinalIgnoreCase)))
            {
                if (explicitlyMutated.Contains(definition.Key)
                    || !next.ContainsKey(definition.Key)
                    || invalidated.Contains(definition.Key))
                    continue;

                ApplyMutation(next, mutations, definition.Key, null);
                invalidated.Add(definition.Key);
                MarkChanged(definition.Key, changed, versions);
                pending.Enqueue(definition.Key);
            }
        }
    }

    private static void ApplyMutation(
        IDictionary<string, string> next,
        IDictionary<string, string?> mutations,
        string key,
        string? value)
    {
        mutations[key] = value;
        if (value is null)
            next.Remove(key);
        else
            next[key] = value;
    }

    private static void MarkChanged(string key, ISet<string> changed, IDictionary<string, long> versions)
    {
        changed.Add(key);
        versions[key] = versions.TryGetValue(key, out var current) ? current + 1 : 1;
    }

    private static string CanonicalText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => throw new InvalidOperationException($"Fact value kind '{value.ValueKind}' cannot be persisted.")
    };
}