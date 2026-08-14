using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Gating;

/// <summary>
/// Snapshot genérico de facts que una verificación consumió al registrarse.
/// El motor compara strings; no conoce roles ni dominio del tenant.
/// </summary>
public static class VerificationSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(IReadOnlyDictionary<string, string> dependencyFacts) =>
        JsonSerializer.Serialize(dependencyFacts, JsonOptions);

    public static bool Matches(string? payloadJson, IReadOnlyDictionary<string, string> currentFacts)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return true;

        var snapshot = JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, expected) in snapshot)
        {
            currentFacts.TryGetValue(key, out var current);
            if (!string.Equals((current ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Toma los valores actuales de los facts indicados ("" si ausente).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Of(
        IReadOnlyDictionary<string, string> facts,
        params string[] keys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = facts.TryGetValue(key, out var value) ? value : string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Construye un snapshot explícito (p. ej. cuando la operation leyó args además de facts).
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromValues(
        params KeyValuePair<string, string>[] pairs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = value ?? string.Empty;
        }

        return result;
    }
}
