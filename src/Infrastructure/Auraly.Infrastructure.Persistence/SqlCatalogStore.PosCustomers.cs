using System.Text.Json;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<long> PricingCursorAsync(
        Guid deviceId, Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await EnsurePosCustomerScopeAsync(connection, deviceId, tenantId, businessId, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Configuration';";
        command.Parameters.Add(P("@BusinessId", businessId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
    public async Task<PosCustomerBootstrapPage> CustomerBootstrapPageAsync(
        Guid deviceId,
        Guid tenantId,
        Guid businessId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await EnsurePosCustomerScopeAsync(connection, deviceId, tenantId, businessId, ct);

        var after = Guid.TryParse(cursor, out var parsed) ? parsed : (Guid?)null;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT ISNULL(MAX(AvailableThroughCursor),0)
            FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';

            SELECT TOP (@Take) {PosCustomerColumns}
            FROM dbo.Customers customer
            JOIN dbo.Parties party ON party.PartyId=customer.PartyId AND party.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=customer.CustomerId
            LEFT JOIN dbo.CounterpartyTaxProfiles taxProfile
              ON taxProfile.BusinessId=customer.BusinessId AND taxProfile.CounterpartyId=customer.CustomerId
            LEFT JOIN dbo.CustomerCreditProfiles credit
              ON credit.BusinessId=customer.BusinessId AND credit.CustomerId=customer.CustomerId
            OUTER APPLY(
              SELECT SUM(receivable.OutstandingAmount) Outstanding
              FROM dbo.Receivables receivable
              WHERE receivable.BusinessId=customer.BusinessId
                AND receivable.CustomerId=customer.CustomerId
                AND receivable.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE customer.BusinessId=@BusinessId AND party.IsActive=1
              AND (@After IS NULL OR customer.CustomerId>@After)
            ORDER BY customer.CustomerId;
            """;
        command.Parameters.AddRange([
            P("@TenantId", tenantId), P("@BusinessId", businessId),
            P("@After", after), P("@Take", pageSize + 1)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var throughCursor = reader.GetInt64(0);
        await reader.NextResultAsync(ct);
        var customers = new List<PosCustomerPricing>();
        while (await reader.ReadAsync(ct)) customers.Add(ReadPosCustomer(reader, 0));
        var hasMore = customers.Count > pageSize;
        if (hasMore) customers.RemoveAt(customers.Count - 1);
        var next = hasMore ? customers[^1].CustomerId.ToString("D") : null;
        return new PosCustomerBootstrapPage(throughCursor, next, hasMore, customers);
    }

    public async Task<PosCustomerDeltaPage> CustomerChangesAsync(
        Guid deviceId,
        Guid tenantId,
        Guid businessId,
        long cursor,
        int pageSize,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await EnsurePosCustomerScopeAsync(connection, deviceId, tenantId, businessId, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Take)
                   change.AvailableThroughCursor,change.ChangeKind,change.EntityId,
                   {PosCustomerColumns}
            FROM dbo.PosSynchronizationOutboxMessages change
            LEFT JOIN dbo.Customers customer
              ON customer.CustomerId=change.EntityId AND customer.BusinessId=change.BusinessId
            LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId AND party.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=customer.CustomerId
            LEFT JOIN dbo.CounterpartyTaxProfiles taxProfile
              ON taxProfile.BusinessId=customer.BusinessId AND taxProfile.CounterpartyId=customer.CustomerId
            LEFT JOIN dbo.CustomerCreditProfiles credit
              ON credit.BusinessId=customer.BusinessId AND credit.CustomerId=customer.CustomerId
            OUTER APPLY(
              SELECT SUM(receivable.OutstandingAmount) Outstanding
              FROM dbo.Receivables receivable
              WHERE receivable.BusinessId=customer.BusinessId
                AND receivable.CustomerId=customer.CustomerId
                AND receivable.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE change.BusinessId=@BusinessId AND change.Stream=N'Customers'
              AND change.AvailableThroughCursor>@Cursor AND change.EntityType=N'Customer'
              AND change.EntityId IS NOT NULL
            ORDER BY change.AvailableThroughCursor;
            """;
        command.Parameters.AddRange([
            P("@TenantId", tenantId), P("@BusinessId", businessId),
            P("@Cursor", cursor), P("@Take", pageSize + 1)
        ]);
        var changes = new List<PosCustomerDelta>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var version = reader.GetInt64(0);
            var kind = reader.IsDBNull(1) ? "Upsert" : reader.GetString(1);
            var customerId = reader.GetGuid(2);
            var customer = reader.IsDBNull(3) || string.Equals(kind, "Tombstone", StringComparison.Ordinal)
                ? null
                : ReadPosCustomer(reader, 3);
            changes.Add(new PosCustomerDelta(version, customer is null ? "Tombstone" : "Upsert", customerId, customer));
        }
        var hasMore = changes.Count > pageSize;
        if (hasMore) changes.RemoveAt(changes.Count - 1);
        return new PosCustomerDeltaPage(
            cursor,
            changes.Count == 0 ? cursor : changes[^1].Version,
            hasMore,
            changes);
    }

    private static async Task EnsurePosCustomerScopeAsync(
        SqlConnection connection,
        Guid deviceId,
        Guid tenantId,
        Guid businessId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM dbo.EnrolledDevices device
            JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
              AND business.TenantId=device.TenantId AND business.IsActive=1
            WHERE device.DeviceId=@DeviceId AND device.TenantId=@TenantId AND device.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@DeviceId", deviceId), P("@TenantId", tenantId), P("@BusinessId", businessId)
        ]);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            throw new CatalogForbiddenException("The device customer scope is invalid.");
    }

    private const string PosCustomerColumns = """
        customer.CustomerId,
        COALESCE(party.NormalizedIdentification,party.Identification,N''),
        COALESCE(party.DisplayName,party.LegalName,party.Identification,N''),
        setting.PriceChannelId,customer.IsActive,customer.RequiresElectronicInvoice,
        COALESCE(taxProfile.AppliesWithholding,CONVERT(BIT,0)),
        COALESCE(taxProfile.Responsibilities,N'[]'),taxProfile.JurisdictionCode,
        COALESCE(credit.IsCreditEnabled,CONVERT(BIT,0)),credit.CreditLimit,
        CASE WHEN credit.CreditLimit IS NULL THEN NULL
             WHEN credit.CreditLimit-COALESCE(balance.Outstanding,0)<0 THEN CONVERT(DECIMAL(19,4),0)
             ELSE credit.CreditLimit-COALESCE(balance.Outstanding,0) END,
        COALESCE(credit.DefaultDueDays,0),setting.ValidFrom,setting.ValidUntil
        """;

    private static PosCustomerPricing ReadPosCustomer(SqlDataReader reader, int offset) => new(
        reader.GetGuid(offset),
        reader.GetString(offset + 1),
        reader.GetString(offset + 2),
        reader.IsDBNull(offset + 3) ? null : reader.GetGuid(offset + 3),
        reader.GetBoolean(offset + 4),
        reader.GetBoolean(offset + 5),
        reader.GetBoolean(offset + 6),
        JsonSerializer.Deserialize<string[]>(reader.GetString(offset + 7)) ?? [],
        reader.IsDBNull(offset + 8) ? null : reader.GetString(offset + 8),
        reader.GetBoolean(offset + 9),
        reader.IsDBNull(offset + 10) ? null : reader.GetDecimal(offset + 10),
        reader.IsDBNull(offset + 11) ? null : reader.GetDecimal(offset + 11),
        reader.GetInt32(offset + 12),
        reader.IsDBNull(offset + 13) ? null : reader.GetFieldValue<DateTimeOffset>(offset + 13),
        reader.IsDBNull(offset + 14) ? null : reader.GetFieldValue<DateTimeOffset>(offset + 14));
}
