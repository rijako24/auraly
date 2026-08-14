using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Operations.Support;

/// <summary>
/// Helpers para construir respuestas JSON estandarizadas de operations.
/// Shape: { "ok": true, "data": {...} } | { "ok": false, "error": { "code", "message", "recoverable" } }
/// </summary>
internal static class OperationJsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static string Ok(object data, params string[] effects)
    {
        if (effects is null || effects.Length == 0)
            return JsonSerializer.Serialize(new { ok = true, data }, Options);

        return JsonSerializer.Serialize(new { ok = true, data, effects }, Options);
    }

    public static string OkWithEvents(object data, string[] effects, string[] events)
    {
        var hasEffects = effects.Length > 0;
        var hasEvents = events.Length > 0;
        return (hasEffects, hasEvents) switch
        {
            (false, false) => JsonSerializer.Serialize(new { ok = true, data }, Options),
            (true, false) => JsonSerializer.Serialize(new { ok = true, data, effects }, Options),
            (false, true) => JsonSerializer.Serialize(new { ok = true, data, events }, Options),
            _ => JsonSerializer.Serialize(new { ok = true, data, effects, events }, Options)
        };
    }

    public static string OkWithLlm(object data, object? llm, params string[] effects)
    {
        if (effects is null || effects.Length == 0)
            return JsonSerializer.Serialize(new { ok = true, data, llm }, Options);

        return JsonSerializer.Serialize(new { ok = true, data, llm, effects }, Options);
    }

    public static string Error(string code, string message, bool recoverable = false) =>
        JsonSerializer.Serialize(new { ok = false, error = new { code, message, recoverable } }, Options);

    public static string ErrorWithLlm(string code, string message, object? llm, bool recoverable = false) =>
        JsonSerializer.Serialize(new { ok = false, error = new { code, message, recoverable }, llm }, Options);
    public static string ErrorWithNextAction(
        string code,
        string message,
        string nextAction,
        object? context = null,
        bool recoverable = true)
    {
        object llm = context is null
            ? new { next_action = nextAction }
            : new { next_action = nextAction, context };

        return ErrorWithLlm(code, message, llm: llm, recoverable: recoverable);
    }

    public static string MissingPrerequisites(params string[] missing) =>
        ErrorWithNextAction(
            "missing_prerequisites",
            "Required data is missing before this action can be performed.",
            "collect_missing_prerequisites",
            new { missing },
            recoverable: true);

    public static bool TryGetString(JsonElement args, string property, out string value)
    {
        value = string.Empty;
        if (!args.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool TryGetBool(JsonElement args, string property, out bool value)
    {
        value = false;
        if (!args.TryGetProperty(property, out var el)) return false;
        if (el.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (el.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }

    public static bool TryGetInt(JsonElement args, string property, out int value)
    {
        value = 0;
        if (!args.TryGetProperty(property, out var el)) return false;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            return true;

        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), out value))
            return true;

        return false;
    }
}
