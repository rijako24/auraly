using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Genera el JSON Schema de set_fact a partir del factSchema del tenant.
/// El enum de <c>key</c> impide al LLM inventar claves fuera del contrato configurado.
/// </summary>
internal static class SetFactParametersSchemaBuilder
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = false
    };

    internal const string FallbackSchema = """
        {
          "type": "object",
          "properties": {
            "key": {
              "type": "string",
              "description": "Short snake_case identifier (e.g. customer_name, baby_age_months, service)"
            },
            "value": {
              "type": "string",
              "description": "Structured value (number, name, date YYYY-MM-DD, time HH:mm — not a full sentence)"
            }
          },
          "required": ["key", "value"]
        }
        """;

    public static string Build(AgentConfig config)
    {
        var userFacts = config.FactSchema
            .Where(e => e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (userFacts.Count == 0)
            return FallbackSchema;

        var keyDescriptions = userFacts
            .Select(e =>
            {
                var typeHint = FormatTypeHint(e);
                return $"{e.Key} ({typeHint}): {e.Label}.";
            });

        var valueHints = userFacts
            .Select(e => $"{e.Key} → {FormatTypeHint(e)}.");

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["key"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = userFacts.Select(e => e.Key).ToArray(),
                    ["description"] =
                        "Canonical fact key from this agent's schema. " +
                        string.Join(" ", keyDescriptions)
                },
                ["value"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] =
                        "Structured value for the selected key (not a full sentence). " +
                        string.Join(" ", valueHints)
                }
            },
            ["required"] = new[] { "key", "value" }
        };

        return JsonSerializer.Serialize(schema, SerializeOptions);
    }

    private static string FormatTypeHint(FactSchemaEntry entry) => entry.Type.ToLowerInvariant() switch
    {
        "number" => "number",
        "date" => "date YYYY-MM-DD",
        "time" => "time HH:mm",
        "phone" => "phone",
        "email" => "email",
        _ => "string"
    };
}
