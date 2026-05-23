using MimosBabySpa.Application.Agents.Facts;

namespace MimosBabySpa.Application.Agents.Gating;

/// <summary>
/// Clave de alcance para verificaciones ligadas a un slot (servicio + fecha + hora).
/// </summary>
public static class SlotVerificationScope
{
    public const string UniversalScope = "*";

    public static string Build(string service, string date, string time)
    {
        var normalizedService = service.Trim().ToLowerInvariant();
        var normalizedDate = date.Trim();
        var normalizedTime = NormalizeTime(time);
        return $"{normalizedService}|{normalizedDate}|{normalizedTime}";
    }

    /// <summary>
    /// Resuelve el scope desde los facts del contexto usando roles semánticos si están disponibles.
    /// Roles usados: "booking.service", "booking.date", "booking.time".
    /// Fallback a lookup directo de facts si no hay schema de roles.
    /// </summary>
    public static string? FromFacts(
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyList<Configuration.FactSchemaEntry>? factSchema = null)
    {
        string? service, date, time;

        if (factSchema is { Count: > 0 })
        {
            var index = new FactRoleIndex(factSchema);
            service = index.GetByRole(facts, "booking.service");
            date    = index.GetByRole(facts, "booking.date");
            time    = index.GetByRole(facts, "booking.time");
        }
        else
        {
            facts.TryGetValue(ConversationFactKeys.Service, out service);
            facts.TryGetValue(ConversationFactKeys.DesiredDate, out date);
            facts.TryGetValue(ConversationFactKeys.DesiredTime, out time);
        }

        if (string.IsNullOrWhiteSpace(service)
            || string.IsNullOrWhiteSpace(date)
            || string.IsNullOrWhiteSpace(time))
        {
            return null;
        }

        return Build(service, date, time);
    }

    private static string NormalizeTime(string time)
    {
        if (TimeOnly.TryParse(time.Trim(), out var parsed))
            return parsed.ToString("HH:mm");

        return time.Trim();
    }
}
