using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resultado parseado de la ejecución de una tool.
/// </summary>
public sealed record ToolExecutionOutcome(
    string RawJson,
    bool IsError,
    IReadOnlyList<string> SideEffects,
    string? ErrorCode = null,
    string? ErrorMessage = null)
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

            string? errorCode = null;
            string? errorMessage = null;
            if (isError && root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("code", out var code))
                    errorCode = code.GetString();
                if (err.TryGetProperty("message", out var msg))
                    errorMessage = msg.GetString();
            }

            return new ToolExecutionOutcome(rawJson, isError, effects, errorCode, errorMessage);
        }
        catch
        {
            return new ToolExecutionOutcome(rawJson, IsError: false, None);
        }
    }

    public bool HasEffect(string name) =>
        SideEffects.Contains(name, StringComparer.OrdinalIgnoreCase);
}
