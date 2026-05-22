namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve precio de checkout (catálogo + política de anticipo) para tools del agente.
/// </summary>
public interface IReservationCheckoutPricing
{
    Task<CheckoutPricingResult?> ResolveAsync(
        Guid businessId,
        string service,
        string? addOnsCsv,
        CancellationToken cancellationToken = default);
}
