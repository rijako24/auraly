using System.Text;
using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Formatea datos de <c>get_service_catalog</c> para el prompt del LLM (referencia, no salida verbatim).
/// </summary>
internal static class CatalogReferenceFormatter
{
    public static string? FormatServicesForPrompt(FlowToolResult? lookupResult)
    {
        if (lookupResult is null || lookupResult.IsError)
            return null;

        if (!lookupResult.TemplateData.TryGetValue("services", out var servicesObj)
            || servicesObj is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        var currency = lookupResult.TemplateData.TryGetValue("currency", out var c)
            ? c?.ToString() ?? "COP"
            : "COP";

        AppendServiceList(sb, servicesObj, currency);
        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    private static void AppendServiceList(StringBuilder sb, object servicesObj, string currency)
    {
        switch (servicesObj)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } arr:
                foreach (var item in arr.EnumerateArray())
                    AppendServiceLine(sb, item, currency);
                break;
            case IEnumerable<object?> list:
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object?> dict)
                        AppendServiceLineFromDict(sb, dict, currency);
                    else if (item is JsonElement el)
                        AppendServiceLine(sb, el, currency);
                }
                break;
        }
    }

    private static void AppendServiceLineFromDict(
        StringBuilder sb,
        Dictionary<string, object?> item,
        string currency)
    {
        if (!item.TryGetValue("name", out var nameObj) || nameObj is null)
            return;

        var name = nameObj.ToString();
        if (string.IsNullOrWhiteSpace(name))
            return;

        item.TryGetValue("price", out var priceObj);
        item.TryGetValue("duration_minutes", out var durationObj);
        item.TryGetValue("description", out var descObj);

        var price = priceObj?.ToString() ?? "";
        var duration = durationObj?.ToString() ?? "";
        var description = descObj?.ToString();

        sb.AppendLine($"- **{name}** — ${price} {currency}, {duration} min");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var desc = description.Length > 600 ? description[..600] + "…" : description;
            sb.AppendLine($"  {desc.Replace('\n', ' ')}");
        }
        sb.AppendLine();
    }

    private static void AppendServiceLine(StringBuilder sb, JsonElement item, string currency)
    {
        var name = GetStringProp(item, "name");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var price = GetStringProp(item, "price") ?? GetNumberProp(item, "price");
        var duration = GetStringProp(item, "duration_minutes") ?? GetNumberProp(item, "duration_minutes");
        var description = GetStringProp(item, "description");

        sb.AppendLine($"- **{name}** — ${price} {currency}, {duration} min");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var desc = description.Length > 600 ? description[..600] + "…" : description;
            sb.AppendLine($"  {desc.Replace('\n', ' ')}");
        }
        sb.AppendLine();
    }

    private static string? GetStringProp(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string? GetNumberProp(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetRawText()
            : null;
}
