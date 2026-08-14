using System.Text.Json;
using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Services;

public static class ReservationCustomAttributes
{
    public const string AttributesPropertyName = "attributes";
    public const string CollectedInfoPropertyName = "collected_info";

    public static string BuildJson(
        IReadOnlyDictionary<string, string>? facts,
        IReadOnlyList<FactSchemaEntry>? factSchema)
    {
        if (facts is null || facts.Count == 0)
            return "{}";

        if (factSchema is not { Count: > 0 })
            return "{}";

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var collectedInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in factSchema)
        {
            if (!facts.TryGetValue(entry.Key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            var label = ResolveLabel(entry);
            var trimmed = value.Trim();
            var key = entry.Key.Trim();

            if (!string.IsNullOrWhiteSpace(key)
                && string.Equals(entry.Source, "user", StringComparison.OrdinalIgnoreCase))
            {
                attributes[key] = trimmed;
            }

            if (entry.ShowInCollectedInfo)
                collectedInfo[label] = trimmed;
        }

        if (attributes.Count == 0 && collectedInfo.Count == 0)
            return "{}";

        var payload = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (attributes.Count > 0)
            payload[AttributesPropertyName] = attributes;
        if (collectedInfo.Count > 0)
            payload[CollectedInfoPropertyName] = collectedInfo;

        return JsonSerializer.Serialize(payload);
    }

    private static string ResolveLabel(FactSchemaEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Label) ? HumanizeKey(entry.Key) : entry.Label.Trim();

    private static string HumanizeKey(string key)
    {
        var cleaned = key.Replace('_', ' ').Replace('.', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? key : cleaned;
    }
}
