using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Helpers para construir respuestas JSON estandarizadas de tools.
/// Shape: { "ok": true, "data": {...} } | { "ok": false, "error": { "code", "message", "hint" } }
/// </summary>
internal static class ToolResultHelper
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

    public static string OkWithLlm(object data, object? llm, params string[] effects)
    {
        if (effects is null || effects.Length == 0)
            return JsonSerializer.Serialize(new { ok = true, data, llm }, Options);

        return JsonSerializer.Serialize(new { ok = true, data, llm, effects }, Options);
    }

    public static string Error(string code, string message, string? hint = null, bool recoverable = false) =>
        JsonSerializer.Serialize(new { ok = false, error = new { code, message, hint, recoverable } }, Options);

    public static string MissingPrerequisites(params string[] missing) =>
        Error("missing_prerequisites",
            "Required data is missing before this action can be performed.",
            $"Collect the following first: {string.Join(", ", missing)}",
            recoverable: true);

    public static bool TryGetString(JsonElement args, string property, out string value)
    {
        value = string.Empty;
        if (!args.TryGetProperty(property, out var el)) return false;
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
