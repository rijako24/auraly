using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed class FiscalStatusSynchronizationNotifier(
    SqlServerConnectionFactory connections,
    IPosSynchronizationOutboxDispatcher dispatcher,
    ILogger<FiscalStatusSynchronizationNotifier> logger)
{
    public async Task DispatchAsync(Guid businessId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId AND IsActive=1;",
                connection);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is Guid tenantId)
                await dispatcher.DispatchPendingAsync(tenantId, businessId, cancellationToken);
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
