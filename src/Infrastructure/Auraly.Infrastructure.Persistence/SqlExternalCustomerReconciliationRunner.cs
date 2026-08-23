using Auraly.Application.Parties;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Auraly.Platform.Application.Commerce;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlExternalCustomerReconciliationRunner(
    SqlServerConnectionFactory connections,
    IExternalCustomerReconciliationStore store,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IExternalCustomerReconciliationRunner
{
    public async Task<int> ReconcilePendingAsync(
        Guid businessId,
        CancellationToken ct = default)
    {
        var tenantId = await ReadTenantIdAsync(businessId, ct);
        var pending = await ReadPendingAsync(businessId, ct);
        var linked = 0;
        foreach (var externalCustomerId in pending)
        {
            var result = await store.ReconcileAsync(
                new ExternalCustomerReconciliationExecution(
                    tenantId,
                    businessId,
                    null,
                    "Integration"),
                externalCustomerId,
                ids.NewId(),
                ids.NewId(),
                ids.NewId(),
                ids.NewId(),
                timeProvider.GetUtcNow(),
                ct);
            if (result.Status == ExternalCustomerReconciliationStatuses.Linked)
                linked++;
        }
        return linked;
    }

    private async Task<Guid> ReadTenantIdAsync(
        Guid businessId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId;";
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid tenantId
            ? tenantId
            : throw new InvalidOperationException("The synchronization business does not exist.");
    }

    private async Task<IReadOnlyCollection<Guid>> ReadPendingAsync(
        Guid businessId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ExternalCommerceCustomerId
            FROM dbo.ExternalCommerceCustomers
            WHERE BusinessId=@BusinessId AND ReconciliationStatus=N'Pending'
            ORDER BY LastSyncedAt,ExternalCommerceCustomerId;
            """;
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        return result;
    }
}
