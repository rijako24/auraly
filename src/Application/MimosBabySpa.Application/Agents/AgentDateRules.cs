namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Validaciones de fecha alineadas al reloj del negocio (AgentToolContext.BusinessToday).
/// </summary>
public static class AgentDateRules
{
    public static bool IsPastDate(DateOnly date, DateOnly businessToday) =>
        date < businessToday;

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParse(value, out date);
}
