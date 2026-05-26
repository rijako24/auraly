namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Contratos de roles semánticos del booking-pack.
/// Son vocabulario del pack, no keys de tenant — cada tenant mapea estos roles en factSchema.
/// </summary>
public static class FactRoles
{
    public const string SessionEngagement = "session.engagement";

    public const string BookingService = "booking.service";
    public const string BookingDate = "booking.date";
    public const string BookingTime = "booking.time";
    public const string BookingAddOns = "booking.addons";

    public const string CustomerName = "customer.name";
    public const string CustomerPhone = "customer.phone";
    public const string CustomerEmail = "customer.email";
}
