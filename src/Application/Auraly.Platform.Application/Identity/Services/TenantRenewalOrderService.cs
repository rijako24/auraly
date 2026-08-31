using Auraly.Contracts.TenantBilling;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantRenewalOrderService(
    ITenantCommercialQuoteService quotes,
    ITenantRenewalOrderStore orders)
{
    public Task<TenantRenewalOrderDto?> GetCurrentAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        orders.GetCurrentAsync(tenantId, cancellationToken);

    public async Task<TenantRenewalOrderDto> ReviseAsync(
        Guid tenantId, Guid userId, TenantQuoteRequest request, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("El tenant y el usuario son obligatorios.");
        var quote = await quotes.QuoteAsync(request, cancellationToken);
        return await orders.CreateRevisionAsync(tenantId, userId, quote, cancellationToken);
    }
}
