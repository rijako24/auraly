using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Combina resolución de precios del catálogo con la política de anticipo del negocio.
/// Fuente única para tools de checkout (resolve_pricing, generate_payment_link).
/// </summary>
public sealed class ReservationCheckoutPricing : IReservationCheckoutPricing
{
    private readonly ReservationPricingResolver _pricing;
    private readonly IBookingPolicyProvider _bookingPolicy;

    public ReservationCheckoutPricing(
        ReservationPricingResolver pricing,
        IBookingPolicyProvider bookingPolicy)
    {
        _pricing = pricing;
        _bookingPolicy = bookingPolicy;
    }

    public async Task<CheckoutPricingResult?> ResolveAsync(
        Guid businessId,
        string service,
        string? addOnsCsv,
        CancellationToken cancellationToken = default)
    {
        var items = new Dictionary<string, string?> { ["service"] = service };

        if (!string.IsNullOrWhiteSpace(addOnsCsv))
            items["add_ons"] = addOnsCsv;

        var pricing = await _pricing.ResolveAsync(businessId, items, cancellationToken);
        if (pricing is null)
            return null;

        var policy = await _bookingPolicy.GetAsync(businessId, cancellationToken);
        var totalCents = (long)(pricing.Total * 100);
        var depositCents = policy.CalculateDepositCents(totalCents);

        return new CheckoutPricingResult(pricing, policy, totalCents, depositCents);
    }
}

public sealed record CheckoutPricingResult(
    PricingResult Pricing,
    BookingPolicyParams Policy,
    long TotalCents,
    long DepositCents)
{
    public bool DepositRequired => Policy.DepositRequired && DepositCents > 0;

    public string BuildServiceDescription()
    {
        var names = Pricing.LineItems.Select(li => li.Name).ToList();
        return names.Count == 0 ? "Reserva" : string.Join(" + ", names);
    }
}
