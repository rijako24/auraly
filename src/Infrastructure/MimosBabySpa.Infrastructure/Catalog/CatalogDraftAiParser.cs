using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.LLM;

using MimosBabySpa.Application.Identity.Interfaces;

namespace MimosBabySpa.Infrastructure.Catalog;

public sealed class CatalogDraftAiParser : ICatalogDraftParser
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<CatalogDraftAiParser> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogDraftAiParser(IChatClient chatClient, ILogger<CatalogDraftAiParser> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CatalogImportServiceLineDto>> ParseAsync(
        string documentText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
            return [];

        var trimmed = documentText.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            var fromJson = TryParseStructuredJson(trimmed);
            if (fromJson.Count > 0) return fromJson;
        }

        if (LooksLikeCsv(trimmed))
        {
            var fromCsv = ParseCsv(trimmed);
            if (fromCsv.Count > 0) return fromCsv;
        }

        return await ParseWithLlmAsync(trimmed, ct);
    }

    private async Task<IReadOnlyList<CatalogImportServiceLineDto>> ParseWithLlmAsync(
        string text, CancellationToken ct)
    {
        const int maxChars = 12000;
        if (text.Length > maxChars)
            text = text[..maxChars] + "\n...[truncado]";

        var system = """
            Eres un extractor de catálogos de servicios para spas y negocios de bienestar.
            Devuelve SOLO un JSON array válido, sin markdown, con objetos:
            {
              "serviceName": "string",
              "description": "string opcional",
              "keywords": "string opcional, palabras o frases separadas por coma para identificar el servicio",
              "durationMinutes": number,
              "price": number en pesos COP,
              "categoryName": "string",
              "serviceType": "Standard" o "AddOn",
              "tier": "Base" | "Premium" | "Deluxe"
            }
            Si no hay duración, usa 60. Si no hay categoría, usa "General".
            """;

        var result = await _chatClient.CompleteAsync(
            [
                ChatMessage.System(system),
                ChatMessage.User($"Extrae servicios de este documento:\n\n{text}")
            ],
            tools: null,
            options: new ChatCompletionOptions { Temperature = 0.2f, MaxTokens = 4000, ForceTextResponse = true },
            cancellationToken: ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
        {
            _logger.LogWarning("Catalog LLM parse failed: {Error}", result.ErrorMessage);
            return [];
        }

        var json = ExtractJsonArray(result.Content);
        if (json is null) return [];

        try
        {
            var items = JsonSerializer.Deserialize<List<CatalogImportServiceLineDto>>(json, JsonOptions)
                ?? [];
            return items
                .Where(s => !string.IsNullOrWhiteSpace(s.ServiceName))
                .Select(NormalizeLine)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize catalog JSON from LLM");
            return [];
        }
    }

    private static string? ExtractJsonArray(string content)
    {
        var match = Regex.Match(content, @"\[[\s\S]*\]", RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private static List<CatalogImportServiceLineDto> TryParseStructuredJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return ParseServiceArray(doc.RootElement);

            if (doc.RootElement.TryGetProperty("services", out var services) &&
                services.ValueKind == JsonValueKind.Array)
                return ParseServiceArray(services);
        }
        catch
        {
            /* fallback to LLM */
        }

        return [];
    }

    private static List<CatalogImportServiceLineDto> ParseServiceArray(JsonElement array)
    {
        var list = new List<CatalogImportServiceLineDto>();
        foreach (var el in array.EnumerateArray())
        {
            var name = GetString(el, "serviceName", "name", "nombre");
            if (string.IsNullOrWhiteSpace(name)) continue;

            list.Add(NormalizeLine(new CatalogImportServiceLineDto
            {
                ServiceName = name,
                Description = GetString(el, "description", "descripcion"),
                Keywords = GetString(el, "keywords", "palabrasClave", "palabras_clave", "tags", "etiquetas"),
                DurationMinutes = GetInt(el, 60, "durationMinutes", "duracion", "duration"),
                Price = GetDecimal(el, "price", "precio"),
                CategoryName = GetString(el, "categoryName", "category", "categoria") ?? "General",
                ServiceType = GetString(el, "serviceType", "tipo") ?? "Standard",
                Tier = GetString(el, "tier") ?? "Base"
            }));
        }

        return list;
    }

    private static List<CatalogImportServiceLineDto> ParseCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var nameIdx = IndexOf(headers, "servicename", "name", "nombre", "servicio");
        if (nameIdx < 0) return [];

        var list = new List<CatalogImportServiceLineDto>();
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= nameIdx) continue;
            var name = cols[nameIdx].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            list.Add(NormalizeLine(new CatalogImportServiceLineDto
            {
                ServiceName = name,
                Description = GetCol(cols, headers, "description", "descripcion"),
                Keywords = GetCol(cols, headers, "keywords", "palabrasclave", "palabras_clave", "tags", "etiquetas"),
                DurationMinutes = int.TryParse(GetCol(cols, headers, "durationminutes", "duracion"), out var d) ? d : 60,
                Price = decimal.TryParse(GetCol(cols, headers, "price", "precio"), out var p) ? p : 0,
                CategoryName = GetCol(cols, headers, "categoryname", "category", "categoria") ?? "General",
                ServiceType = GetCol(cols, headers, "servicetype", "tipo") ?? "Standard",
                Tier = GetCol(cols, headers, "tier") ?? "Base"
            }));
        }

        return list;
    }

    private static bool LooksLikeCsv(string text) =>
        text.Contains(',') && text.Split('\n').Length >= 2;

    private static CatalogImportServiceLineDto NormalizeLine(CatalogImportServiceLineDto line) => new()
    {
        ServiceName = line.ServiceName.Trim(),
        Description = line.Description?.Trim(),
        Keywords = line.Keywords?.Trim(),
        CategoryName = string.IsNullOrWhiteSpace(line.CategoryName) ? "General" : line.CategoryName.Trim(),
        ServiceType = NormalizeServiceType(line.ServiceType),
        Tier = NormalizeTier(line.Tier),
        DurationMinutes = line.DurationMinutes > 0 ? line.DurationMinutes : 60,
        Price = line.Price,
        Selected = true
    };

    private static string NormalizeServiceType(string? value) =>
        value?.Equals("AddOn", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("addon", StringComparison.OrdinalIgnoreCase) == true
            ? "AddOn"
            : "Standard";

    private static string NormalizeTier(string? value) => value?.ToLowerInvariant() switch
    {
        "premium" => "Premium",
        "deluxe" => "Deluxe",
        _ => "Base"
    };

    private static int IndexOf(string[] headers, params string[] names)
    {
        for (var i = 0; i < headers.Length; i++)
            if (names.Any(n => headers[i] == n))
                return i;
        return -1;
    }

    private static string? GetCol(string[] cols, string[] headers, params string[] names)
    {
        var idx = IndexOf(headers, names);
        return idx >= 0 && idx < cols.Length ? cols[idx].Trim() : null;
    }

    private static string? GetString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        return null;
    }

    private static int GetInt(JsonElement el, int fallback, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p)) continue;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)) return v;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
        }
        return fallback;
    }

    private static decimal GetDecimal(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p)) continue;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var v)) return v;
            if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var s)) return s;
        }
        return 0;
    }
}
