using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Api;

public sealed class FiscalStatusSynchronizationNotifier(
    IServiceScopeFactory scopes,
    IPosSynchronizationOutboxDispatcher dispatcher,
    ILogger<FiscalStatusSynchronizationNotifier> logger)
{
    public async Task DispatchAsync(Guid businessId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantId = await db.Businesses
                .AsNoTracking()
                .Where(business => business.BusinessId == businessId && business.IsActive)
                .Select(business => (Guid?)business.TenantId)
                .SingleOrDefaultAsync(cancellationToken);
            if (tenantId is { } activeTenantId)
                await dispatcher.DispatchPendingAsync(activeTenantId, businessId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Fiscal status notification dispatch failed for business {BusinessId}; the outbox remains durable.",
                businessId);
        }
    }
}
