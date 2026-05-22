using System.Text.Json;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resultado parseado de la ejecución de una tool.
///
/// Encapsula el JSON crudo que vuelve al LLM, el estado ok/error y
/// los side-effects declarados por la tool en el campo "effects".
/// Aísla al orquestador del parsing JSON ad-hoc.
///
/// Shape esperada de una tool:
///   ok=true  → { "ok": true,  "data": {...}, "effects": ["reservation_created"] }
///   ok=false → { "ok": false, "error": { "code", "message", "hint" } }
/// </summary>
internal sealed record ToolExecutionOutcome(
    string RawJson,
    bool IsError,
    IReadOnlyList<string> SideEffects)
{
    private static readonly IReadOnlyList<string> None = [];

    public static ToolExecutionOutcome Parse(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var isError = root.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.False;

            IReadOnlyList<string> effects = None;
            if (!isError
                && root.TryGetProperty("effects", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                effects = arr.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            return new ToolExecutionOutcome(rawJson, isError, effects);
        }
        catch
        {
            return new ToolExecutionOutcome(rawJson, IsError: false, None);
        }
    }

    public bool HasEffect(string name) =>
        SideEffects.Contains(name, StringComparer.OrdinalIgnoreCase);
}
