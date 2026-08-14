using System.Text;

namespace Auraly.Platform.Application.Time;

/// <summary>
/// Anclas temporales calculadas para inyectar en el system prompt del turno.
/// </summary>
public sealed class TemporalReferenceContext
{
    private static readonly string[] WeekdayNames =
    [
        "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"
    ];

    public required string TimeZoneId { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required DateOnly Today { get; init; }
    public required IReadOnlyList<TemporalDayEntry> UpcomingDays { get; init; }

    public string ToPromptBlock()
    {
        var sb = new StringBuilder();
        var weekday = WeekdayNames[(int)Now.DayOfWeek];
        var tomorrow = Today.AddDays(1);
        var dayAfterTomorrow = Today.AddDays(2);

        sb.AppendLine("## CONTEXTO TEMPORAL");
        sb.AppendLine($"Zona horaria del negocio: **{TimeZoneId}**");
        sb.AppendLine(
            $"Ahora: **{Now:yyyy-MM-dd HH:mm}** ({weekday})");
        sb.AppendLine();
        sb.AppendLine("Anclas para interpretar expresiones del cliente (usa **YYYY-MM-DD** al llamar tools):");
        sb.AppendLine($"- hoy → {Today:yyyy-MM-dd}");
        sb.AppendLine($"- mañana → {tomorrow:yyyy-MM-dd}");
        sb.AppendLine($"- pasado mañana → {dayAfterTomorrow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("Calendario de referencia (próximos días):");
        foreach (var day in UpcomingDays)
            sb.AppendLine($"- {day.IsoDate} — {day.WeekdayName}{day.RelativeLabel}");

        sb.AppendLine();
        sb.AppendLine(
            "Convierte siempre fechas relativas usando estas anclas antes de ejecutar acciones " +
            "que dependan de fecha u hora. Usa **YYYY-MM-DD** para fechas y **HH:mm** (24h) para horas.");
        sb.AppendLine(
            "Para describir reservas existentes, calcula la etiqueta actual desde la fecha absoluta " +
            "de la reserva y la fecha local de este turno.");
        sb.AppendLine(
            "Las expresiones relativas del historial pertenecen al dia en que fueron escritas; " +
            "la autoridad temporal de este turno es este bloque.");

        return sb.ToString().TrimEnd();
    }

    public sealed record TemporalDayEntry(
        string IsoDate,
        string WeekdayName,
        string RelativeLabel);

    internal static string FormatWeekday(DayOfWeek dayOfWeek) =>
        WeekdayNames[(int)dayOfWeek];

    internal static string FormatRelativeLabel(DateOnly date, DateOnly today)
    {
        var delta = date.DayNumber - today.DayNumber;
        return delta switch
        {
            0 => " (hoy)",
            1 => " (mañana)",
            2 => " (pasado mañana)",
            _ => string.Empty
        };
    }
}
