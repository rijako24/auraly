using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Services;

public static class ReservationCustomAttributes
{
    public static string BuildJson(
        IReadOnlyDictionary<string, string>? facts,
        IReadOnlyList<FactSchemaEntry>? factSchema)
    {
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (facts is null || facts.Count == 0)
            return "{}";

        if (factSchema is not { Count: > 0 })
            return "{}";

        foreach (var entry in factSchema)
        {
            if (!entry.ShowInCollectedInfo
                || !facts.TryGetValue(entry.Key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            custom[ResolveLabel(entry)] = value.Trim();
        }

        return custom.Count == 0 ? "{}" : JsonSerializer.Serialize(custom);
    }

    private static string ResolveLabel(FactSchemaEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Label) ? HumanizeKey(entry.Key) : entry.Label.Trim();

    private static string HumanizeKey(string key)
    {
        var cleaned = key.Replace('_', ' ').Replace('.', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? key : cleaned;
    }
}
