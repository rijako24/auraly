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

    public static string? FromFacts(IReadOnlyDictionary<string, string> facts)
    {
        var service = ConversationFactKeys.Get(facts, ConversationFactKeys.Service);
        var date = ConversationFactKeys.Get(facts, ConversationFactKeys.DesiredDate);
        var time = ConversationFactKeys.Get(facts, ConversationFactKeys.DesiredTime);

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
