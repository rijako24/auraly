namespace Auraly.Platform.Application.Agents;

/// <summary>
/// Claves canónicas de hechos (Facts) usadas por el vertical de booking.
/// Cualquier otro hecho es libre y tenant-specific.
/// </summary>
public static class ConversationFactKeys
{
    public const string CustomerName = "customer_name";
    public const string CustomerPhone = "customer_phone";
    public const string CustomerEmail = "customer_email";
    public const string Service = "service";
    public const string DesiredDate = "desired_date";
    public const string DesiredTime = "desired_time";
    public const string AddOns = "add_ons";

    public static string? Get(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
