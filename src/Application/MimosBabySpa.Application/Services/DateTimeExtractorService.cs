using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class DateTimeExtractorService : IDateTimeExtractorService
{
    private readonly ILogger<DateTimeExtractorService> _logger;

    public DateTimeExtractorService(ILogger<DateTimeExtractorService> logger)
    {
        _logger = logger;
    }

    public DateTime? ExtractDate(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var normalizedMessage = message.ToLowerInvariant();
        var today = DateTime.UtcNow.AddHours(-5); // Colombia UTC-5
        var todayDate = today.Date;

        // Patrones de fecha comunes en español
        // "mañana", "pasado mañana", "el lunes", "el martes", etc.
        if (normalizedMessage.Contains("mañana") && !normalizedMessage.Contains("pasado"))
        {
            return todayDate.AddDays(1);
        }

        if (normalizedMessage.Contains("pasado mañana"))
        {
            return todayDate.AddDays(2);
        }

        // Días de la semana
        var dayOfWeekMap = new Dictionary<string, int>
        {
            { "lunes", 1 }, { "martes", 2 }, { "miércoles", 3 }, { "miercoles", 3 },
            { "jueves", 4 }, { "viernes", 5 }, { "sábado", 6 }, { "sabado", 6 },
            { "domingo", 0 }
        };

        foreach (var day in dayOfWeekMap)
        {
            if (normalizedMessage.Contains(day.Key))
            {
                var targetDayOfWeek = (DayOfWeek)day.Value;
                var daysUntil = ((int)targetDayOfWeek - (int)todayDate.DayOfWeek + 7) % 7;
                if (daysUntil == 0) daysUntil = 7; // Si es hoy, tomar el próximo
                return todayDate.AddDays(daysUntil);
            }
        }

        // Fechas explícitas: "15 de enero", "15/01/2025", "2025-01-15", etc.
        // Patrón: DD/MM/YYYY o DD-MM-YYYY
        var datePattern1 = new Regex(@"(\d{1,2})[/-](\d{1,2})[/-](\d{4})");
        var match1 = datePattern1.Match(message);
        if (match1.Success)
        {
            if (int.TryParse(match1.Groups[1].Value, out var day) &&
                int.TryParse(match1.Groups[2].Value, out var month) &&
                int.TryParse(match1.Groups[3].Value, out var year))
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch { }
            }
        }

        // Patrón: YYYY-MM-DD
        var datePattern2 = new Regex(@"(\d{4})-(\d{1,2})-(\d{1,2})");
        var match2 = datePattern2.Match(message);
        if (match2.Success)
        {
            if (int.TryParse(match2.Groups[1].Value, out var year) &&
                int.TryParse(match2.Groups[2].Value, out var month) &&
                int.TryParse(match2.Groups[3].Value, out var day))
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch { }
            }
        }

        // Patrón: "15 de enero" o "15 enero"
        var monthMap = new Dictionary<string, int>
        {
            { "enero", 1 }, { "febrero", 2 }, { "marzo", 3 }, { "abril", 4 },
            { "mayo", 5 }, { "junio", 6 }, { "julio", 7 }, { "agosto", 8 },
            { "septiembre", 9 }, { "octubre", 10 }, { "noviembre", 11 }, { "diciembre", 12 }
        };

        var datePattern3 = new Regex(@"(\d{1,2})\s+(?:de\s+)?(enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|octubre|noviembre|diciembre)");
        var match3 = datePattern3.Match(normalizedMessage);
        if (match3.Success)
        {
            if (int.TryParse(match3.Groups[1].Value, out var day) &&
                monthMap.TryGetValue(match3.Groups[2].Value, out var month))
            {
                var year = todayDate.Year;
                // Si la fecha ya pasó este año, tomar el próximo año
                try
                {
                    var candidateDate = new DateTime(year, month, day);
                    if (candidateDate < todayDate)
                        candidateDate = candidateDate.AddYears(1);
                    return candidateDate;
                }
                catch { }
            }
        }

        return null;
    }

    public TimeSpan? ExtractTime(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var normalizedMessage = message.ToLowerInvariant();

        // Patrón: HH:mm (24 horas) o HH:mm AM/PM
        var timePattern1 = new Regex(@"(\d{1,2}):(\d{2})\s*(am|pm)?", RegexOptions.IgnoreCase);
        var match1 = timePattern1.Match(message);
        if (match1.Success)
        {
            if (int.TryParse(match1.Groups[1].Value, out var hour) &&
                int.TryParse(match1.Groups[2].Value, out var minute))
            {
                var amPm = match1.Groups[3].Value.ToLowerInvariant();
                if (amPm == "pm" && hour < 12)
                    hour += 12;
                else if (amPm == "am" && hour == 12)
                    hour = 0;

                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                {
                    return new TimeSpan(hour, minute, 0);
                }
            }
        }

        // Patrón: "las 3", "a las 3", "a las 3 de la tarde", etc.
        var timePattern2 = new Regex(@"(?:a\s+)?las\s+(\d{1,2})(?:\s+(?:de\s+la\s+)?(mañana|tarde|noche))?");
        var match2 = timePattern2.Match(normalizedMessage);
        if (match2.Success)
        {
            if (int.TryParse(match2.Groups[1].Value, out var hour))
            {
                var period = match2.Groups[2].Value;
                if (period.Contains("tarde") || period.Contains("noche"))
                {
                    if (hour < 12) hour += 12;
                }
                else if (period.Contains("mañana"))
                {
                    if (hour == 12) hour = 0;
                }
                else
                {
                    // Sin periodo específico, asumir formato 24h o contexto
                    // Por defecto, si es < 12 y no hay contexto, podría ser mañana o tarde
                    // Por seguridad, mantener como está si está en rango válido
                }

                if (hour >= 0 && hour < 24)
                {
                    return new TimeSpan(hour, 0, 0);
                }
            }
        }

        return null;
    }

    public bool ContainsDateTime(string message)
    {
        return ExtractDate(message).HasValue || ExtractTime(message).HasValue;
    }
}
