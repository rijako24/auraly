using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Extractor de fallback con reglas determinísticas mínimas.
///
/// Se activa cuando el LLM falla validación. Usa el LoadedBusinessContext ya cargado
/// (sin queries adicionales a BD). Extrae fechas/horas básicas con regex y aplica
/// los patrones de validación configurados por el negocio para sus atributos.
/// Multitenant y genérico.
/// </summary>
public class FallbackExtractor : IFallbackExtractor
{
    private readonly ILogger<FallbackExtractor> _logger;

    public FallbackExtractor(ILogger<FallbackExtractor> logger)
    {
        _logger = logger;
    }

    public Task<StructuredExtractionResponse> ExtractAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        StructuredExtractionResponse? llmAttempt,
        CancellationToken cancellationToken = default)
    {
        var response = new StructuredExtractionResponse
        {
            ExtractedFields = new List<ExtractedField>(),
            Ambiguities     = new List<CompactAmbiguity>(),
            Intentions      = llmAttempt?.Intentions ?? new ExtractionIntentions()
        };

        var messageLower = userMessage.ToLowerInvariant();

        // 1. Atributos del negocio vía patrones configurados (sin query a BD)
        foreach (var (key, def) in businessContext.Attributes)
        {
            if (string.IsNullOrEmpty(def.ValidationPattern)) continue;

            var match = Regex.Match(userMessage, def.ValidationPattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName  = $"Attribute:{key}",
                Value      = value.Trim(),
                FieldType  = MapAttributeType(def.Type),
                Confidence = 0.8
            });
        }

        // 2. Confirmación explícita (genérica)
        // Excluir "está bien": es ambiguo — "a las 11 está bien" = elección de horario, no confirmación.
        // El FlowEngine solo alcanza ConfirmingBooking cuando todos los datos están completos.
        if (Regex.IsMatch(messageLower, @"\b(sí|si|confirmo|adelante|ok|vale|perfecto|de acuerdo)\b"))
            response.Intentions.UserConfirmedBooking = true;

        // 3. Cancelación explícita (genérica)
        if (Regex.IsMatch(messageLower, @"\b(cancel|mejor no|cambié de opinión|no quiero)\b"))
            response.Intentions.UserWantsToCancel = true;

        // 4. Fechas temporales básicas
        ExtractTemporalDates(messageLower, response);

        // 5. Horas (formato 12h y 24h)
        ExtractTimes(userMessage, response);

        _logger.LogInformation(
            "Fallback: {FieldCount} campos extraídos, confidence avg {Conf:F2}",
            response.ExtractedFields.Count,
            response.ExtractedFields.Any() ? response.ExtractedFields.Average(f => f.Confidence) : 0.0);

        return Task.FromResult(response);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────

    private static void ExtractTemporalDates(string messageLower, StructuredExtractionResponse response)
    {
        // Evitar duplicar si el LLM ya extrajo fecha
        if (response.ExtractedFields.Any(f => f.FieldName == "DesiredDate")) return;

        var today = DateOnly.FromDateTime(DateTime.Now);

        if (messageLower.Contains("pasado mañana") || messageLower.Contains("pasado manana"))
            AddDate(response, today.AddDays(2), "pasado mañana");
        else if (messageLower.Contains("mañana") || messageLower.Contains("manana"))
            AddDate(response, today.AddDays(1), "mañana");
        else if (messageLower.Contains("hoy"))
            AddDate(response, today, "hoy");
        else
            TryExtractWeekday(messageLower, response);
    }

    private static void AddDate(StructuredExtractionResponse response, DateOnly date, string source)
    {
        response.ExtractedFields.Add(new ExtractedField
        {
            FieldName  = "DesiredDate",
            Value      = date.ToString("yyyy-MM-dd"),
            FieldType  = FieldType.Date,
            Confidence = 0.9
        });
    }

    private static void TryExtractWeekday(string messageLower, StructuredExtractionResponse response)
    {
        var days = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["lunes"]     = DayOfWeek.Monday,
            ["martes"]    = DayOfWeek.Tuesday,
            ["miércoles"] = DayOfWeek.Wednesday,
            ["miercoles"] = DayOfWeek.Wednesday,
            ["jueves"]    = DayOfWeek.Thursday,
            ["viernes"]   = DayOfWeek.Friday,
            ["sábado"]    = DayOfWeek.Saturday,
            ["sabado"]    = DayOfWeek.Saturday,
            ["domingo"]   = DayOfWeek.Sunday
        };

        foreach (var (name, dow) in days)
        {
            if (!messageLower.Contains(name)) continue;

            var next = NextWeekday(DateTime.Now, dow);
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName  = "DesiredDate",
                Value      = DateOnly.FromDateTime(next).ToString("yyyy-MM-dd"),
                FieldType  = FieldType.Date,
                Confidence = 0.8
            });
            break;
        }
    }

    private static DateTime NextWeekday(DateTime from, DayOfWeek target)
    {
        var daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(daysUntil == 0 ? 7 : daysUntil);
    }

    private static void ExtractTimes(string userMessage, StructuredExtractionResponse response)
    {
        if (response.ExtractedFields.Any(f => f.FieldName == "DesiredTime")) return;

        // Hora PM: "3pm", "3 de la tarde"
        var pmMatch = Regex.Match(userMessage, @"\b(\d{1,2})\s*(?:pm|de la tarde|de la noche)\b", RegexOptions.IgnoreCase);
        if (pmMatch.Success && int.TryParse(pmMatch.Groups[1].Value, out var hour))
        {
            var h24 = hour == 12 ? 12 : (hour < 12 ? hour + 12 : hour);
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName  = "DesiredTime",
                Value      = $"{h24:D2}:00",
                FieldType  = FieldType.Time,
                Confidence = 0.85
            });
            return;
        }

        // Hora AM: "9am", "9 de la mañana"
        var amMatch = Regex.Match(userMessage, @"\b(\d{1,2})\s*(?:am|de la mañana|de la manana)\b", RegexOptions.IgnoreCase);
        if (amMatch.Success && int.TryParse(amMatch.Groups[1].Value, out var hourAm))
        {
            var h24 = hourAm == 12 ? 0 : hourAm;
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName  = "DesiredTime",
                Value      = $"{h24:D2}:00",
                FieldType  = FieldType.Time,
                Confidence = 0.85
            });
            return;
        }

        // Formato 24h: "15:00", "9:30"
        var h24Match = Regex.Match(userMessage, @"\b(\d{1,2}):(\d{2})\b");
        if (h24Match.Success
            && int.TryParse(h24Match.Groups[1].Value, out var hh)
            && int.TryParse(h24Match.Groups[2].Value, out var mm)
            && hh is >= 0 and < 24 && mm is >= 0 and < 60)
        {
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName  = "DesiredTime",
                Value      = $"{hh:D2}:{mm:D2}",
                FieldType  = FieldType.Time,
                Confidence = 0.9
            });
        }
    }

    private static FieldType MapAttributeType(AttributeType t) => t switch
    {
        AttributeType.Number => FieldType.Number,
        AttributeType.Date   => FieldType.Date,
        AttributeType.Time   => FieldType.Time,
        AttributeType.Email  => FieldType.Email,
        _                    => FieldType.Text
    };
}
