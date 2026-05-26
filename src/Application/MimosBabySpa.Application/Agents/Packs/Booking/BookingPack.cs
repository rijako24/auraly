namespace MimosBabySpa.Application.Agents.Packs.Booking;

public sealed class BookingPack : IToolCapabilityPack
{
    public string PackId => BookingPackIds.Booking;

    public IReadOnlyList<string> ToolNames { get; } =
    [
        "set_fact",
        "get_service_catalog",
        "check_availability",
        "prepare_checkout",
        "create_reservation",
        "assign_paid_slot",
        "reschedule_reservation",
        "suspend_reservation",
        "verify_payment",
        "generate_payment_link",
        "resolve_pricing"
    ];

    public IReadOnlyDictionary<string, string> DefaultTemplates =>
        BookingDefaultTemplates.All;
}
