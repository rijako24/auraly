using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Resolución de keys de facts desde configuración del tenant (schema + stage.collects).
/// </summary>
public static class FactSchemaPrompt
{
    public static IReadOnlyList<string> ResolveCollectKeys(
        IReadOnlyList<FactSchemaEntry> schema,
        IReadOnlyList<string> stageCollects)
    {
        if (stageCollects.Count == 0)
            return [];

        return stageCollects
            .Where(c => !c.StartsWith("result:", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<FactSchemaEntry> EntriesForKeys(
        IReadOnlyList<FactSchemaEntry> schema,
        IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return [];

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return schema
            .Where(e => keySet.Contains(e.Key))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> MissingUserFactKeys(
        IReadOnlyList<FactSchemaEntry> schema,
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, string> facts)
    {
        var entries = EntriesForKeys(schema, keys);
        return entries
            .Where(e => e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Where(e => !facts.TryGetValue(e.Key, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(e => e.Key)
            .ToList();
    }

}
