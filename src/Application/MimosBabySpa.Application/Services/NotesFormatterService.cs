using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación del servicio para formatear información adicional de reservas
/// </summary>
public class NotesFormatterService : INotesFormatterService
{
    private readonly ILogger<NotesFormatterService> _logger;

    public NotesFormatterService(ILogger<NotesFormatterService> logger)
    {
        _logger = logger;
    }

    public string FormatNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        try
        {
            // Intentar parsear el JSON
            var jsonDoc = JsonDocument.Parse(notes);
            var root = jsonDoc.RootElement;

            // Si es un objeto JSON, formatear cada propiedad
            if (root.ValueKind == JsonValueKind.Object)
            {
                var formattedLines = new List<string> { "\n\nInformación adicional:" };
                
                foreach (var property in root.EnumerateObject())
                {
                    var key = property.Name;
                    var value = GetPropertyValue(property.Value);
                    
                    // Formatear la clave a formato legible
                    var displayKey = FormatKey(key);
                    formattedLines.Add($"{displayKey}: {value}");
                }

                return string.Join("\n", formattedLines);
            }
            
            // Si no es un objeto JSON válido, mostrar el texto tal cual
            return $"\n\nInformación adicional: {notes}";
        }
        catch (JsonException ex)
        {
            // Si no es JSON válido, loggear y mostrar el texto tal cual
            _logger.LogWarning(ex, "No se pudo parsear el JSON de notes: {Notes}", notes);
            return $"\n\nInformación adicional: {notes}";
        }
    }

    private static string GetPropertyValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "Sí",
            JsonValueKind.False => "No",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static string FormatKey(string key)
    {
        // Convertir camelCase o snake_case a formato legible de forma genérica
        // Ejemplo: "customerName" -> "Customer Name", "baby_age_months" -> "Baby Age Months"
        
        // Convertir snake_case a espacios
        var withSpaces = key.Replace("_", " ");
        
        // Convertir camelCase a palabras separadas
        var words = System.Text.RegularExpressions.Regex.Replace(
            withSpaces,
            @"([a-z])([A-Z])",
            "$1 $2"
        );

        // Capitalizar primera letra de cada palabra
        var wordsArray = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var capitalizedWords = wordsArray.Select(word => 
            char.ToUpper(word[0]) + word.Substring(1).ToLower()
        );

        return string.Join(" ", capitalizedWords);
    }
}
