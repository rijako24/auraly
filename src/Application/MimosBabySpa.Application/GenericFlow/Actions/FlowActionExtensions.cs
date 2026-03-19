namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Helper extension methods for action input dictionaries.
/// </summary>
public static class FlowActionExtensions
{
    public static string? GetString(this Dictionary<string, object?> inputs, string key) =>
        inputs.TryGetValue(key, out var v) ? v?.ToString() : null;

    public static bool GetBool(this Dictionary<string, object?> inputs, string key, bool defaultValue = false) =>
        inputs.TryGetValue(key, out var v) && bool.TryParse(v?.ToString(), out var b) ? b : defaultValue;

    public static int GetInt(this Dictionary<string, object?> inputs, string key, int defaultValue = 0) =>
        inputs.TryGetValue(key, out var v) && int.TryParse(v?.ToString(), out var i) ? i : defaultValue;
}
