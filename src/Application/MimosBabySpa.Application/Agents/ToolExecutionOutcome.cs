using System.Text.Json;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Parsed tool execution result.
/// Expected success shape: { "ok": true, "data": {...}, "effects": ["request_completed"], "events": ["reservation_created"] }
/// </summary>
internal sealed record ToolExecutionOutcome(
    string RawJson,
    bool IsError,
    IReadOnlyList<string> SideEffects,
    IReadOnlyList<string> Events,
    string? ErrorCode = null,
    bool Recoverable = false)
{
    private static readonly IReadOnlyList<string> None = [];

    public bool IsRecoverableError => IsError && Recoverable;

    public static ToolExecutionOutcome Parse(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var isError = root.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.False;

            string? errorCode = null;
            var recoverable = false;
            if (isError && root.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("code", out var codeEl))
                    errorCode = codeEl.GetString();

                if (errorObj.TryGetProperty("recoverable", out var recoverableEl)
                    && recoverableEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    recoverable = recoverableEl.GetBoolean();
                }
            }

            var effects = !isError ? ReadStringArray(root, "effects") : None;
            var events = !isError ? ReadStringArray(root, "events") : None;

            return new ToolExecutionOutcome(rawJson, isError, effects, events, errorCode, recoverable);
        }
        catch
        {
            return new ToolExecutionOutcome(rawJson, IsError: false, None, None);
        }
    }

    public bool HasEffect(string name) =>
        SideEffects.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return None;

        return arr.EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}