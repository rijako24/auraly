using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class OperationsToolParsing
{
    public static bool TryGetDate(JsonElement args, string property, DateOnly today, out DateOnly date)
    {
        date = default;
        if (!ToolResultHelper.TryGetString(args, property, out var raw))
            return false;

        raw = raw.Trim().ToLowerInvariant();
        if (raw is "today" or "hoy")
        {
            date = today;
            return true;
        }

        if (raw is "tomorrow" or "manana" or "ma�ana")
        {
            date = today.AddDays(1);
            return true;
        }

        return DateOnly.TryParse(raw, out date);
    }

    public static bool TryGetTime(JsonElement args, string property, out TimeSpan time)
    {
        time = default;
        return ToolResultHelper.TryGetString(args, property, out var raw)
            && TimeSpan.TryParse(raw.Trim(), out time);
    }

    public static string NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());

    public static DateTime StartOfDay(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public static DateTime EndOfDayExclusive(DateOnly date) => date.AddDays(1).ToDateTime(TimeOnly.MinValue);

    public static DateTime EndOfDayInclusive(DateOnly date) => date.ToDateTime(TimeOnly.MaxValue);
}
