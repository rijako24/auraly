using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Operations.Internal;

internal static class OperationJsonResult
{
    public static OperationOutcome Parse(string json, string successCode)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            var data = root.TryGetProperty("data", out var value)
                ? value.Clone()
                : EmptyObject();
            var effects = ReadStrings(root, "effects")
                .Select<string, OperationEffect?>(effect => effect switch
                {
                    OperationEffectNames.RequestCompleted => new CompleteRequestOperationEffect(),
                    OperationEffectNames.EscalatedToHuman => new EscalateHumanOperationEffect(),
                    _ => null
                })
                .Where(effect => effect is not null)
                .Cast<OperationEffect>()
                .ToList();
            return OperationOutcome.Ok(successCode, data, effects: effects, events: ReadStrings(root, "events"));
        }

        var error = root.TryGetProperty("error", out var configured) ? configured : default;
        var code = Read(error, "code") ?? "operation_failed";
        var message = Read(error, "message") ?? "The operation could not be completed.";
        var recoverable = error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("recoverable", out var flag)
            && flag.ValueKind == JsonValueKind.True;
        return OperationOutcome.Fail(code, message, recoverable, context: root.Clone());
    }

    private static string? Read(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string property) =>
        root.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
            : [];
    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
