using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Resultado parseado de la ejecución de una tool por el FlowEngine.
/// Extiende ToolExecutionOutcome con acceso tipado a campos de data y soporte de templates.
/// </summary>
public sealed class FlowToolResult
{
    public bool IsError { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorHint { get; init; }
    public IReadOnlyList<string> SideEffects { get; init; } = [];

    /// <summary>ID del template Handlebars que la tool indica para presentar sus datos.</summary>
    public string? TemplateId { get; init; }

    /// <summary>Datos para renderizar el template (extraídos de data.template_data en el rawJson).</summary>
    public IReadOnlyDictionary<string, object?> TemplateData { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Todos los campos de data para resolución de refs @result.X.</summary>
    private readonly IReadOnlyDictionary<string, JsonElement> _fields;

    public FlowToolResult(
        bool isError,
        string? errorCode,
        string? errorMessage,
        string? errorHint,
        IReadOnlyList<string> sideEffects,
        string? templateId,
        IReadOnlyDictionary<string, object?> templateData,
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        IsError = isError;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ErrorHint = errorHint;
        SideEffects = sideEffects;
        TemplateId = templateId;
        TemplateData = templateData;
        _fields = fields;
    }

    public string? GetString(string key) =>
        _fields.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    public bool? GetBool(string key)
    {
        if (!_fields.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public bool HasEffect(string name) =>
        SideEffects.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static FlowToolResult FromError(string code, string message, string? hint = null) =>
        new(true, code, message, hint, [], null,
            new Dictionary<string, object?>(),
            new Dictionary<string, JsonElement>());

    public static FlowToolResult Parse(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var isError = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False;

            string? errorCode = null, errorMessage = null, errorHint = null;
            if (isError && root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("code", out var c)) errorCode = c.GetString();
                if (err.TryGetProperty("message", out var m)) errorMessage = m.GetString();
                if (err.TryGetProperty("hint", out var h)) errorHint = h.GetString();
                return new FlowToolResult(true, errorCode, errorMessage, errorHint, [],
                    null, new Dictionary<string, object?>(), new Dictionary<string, JsonElement>());
            }

            IReadOnlyList<string> effects = [];
            if (root.TryGetProperty("effects", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                effects = arr.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            string? templateId = null;
            var templateData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in data.EnumerateObject())
                    fields[prop.Name] = prop.Value.Clone();

                if (data.TryGetProperty("template_id", out var tid))
                    templateId = tid.GetString();

                if (data.TryGetProperty("template_data", out var td) && td.ValueKind == JsonValueKind.Object)
                    ExtractTemplateData(td, templateData);
                else
                    ExtractTemplateData(data, templateData);
            }

            return new FlowToolResult(false, null, null, null, effects,
                templateId, templateData, fields);
        }
        catch (Exception ex)
        {
            return new FlowToolResult(true, "parse_error", ex.Message, null, [],
                null, new Dictionary<string, object?>(), new Dictionary<string, JsonElement>());
        }
    }

    private static void ExtractTemplateData(JsonElement element, Dictionary<string, object?> target)
    {
        foreach (var prop in element.EnumerateObject())
        {
            target[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? (object?)i : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => ParseArray(prop.Value),
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }
    }

    private static List<object?> ParseArray(JsonElement arr)
    {
        var list = new List<object?>();
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.TryGetInt32(out var i) ? (object?)i : item.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => ParseObject(item),
                _ => null
            });
        }
        return list;
    }

    private static Dictionary<string, object?> ParseObject(JsonElement obj)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        ExtractTemplateData(obj, d);
        return d;
    }
}
