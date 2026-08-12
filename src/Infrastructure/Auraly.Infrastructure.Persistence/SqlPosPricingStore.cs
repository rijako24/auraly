using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<PosPricingSnapshot> PricingSnapshotAsync(
        Guid deviceId,
        Guid tenantId,
        Guid businessId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.EnrolledDevices d
              JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
                AND b.TenantId=d.TenantId AND b.IsActive=1
              WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId
                AND d.IsActive=1)
              THROW 51020,'The device pricing scope is invalid.',1;

            SELECT i.PriceListId,i.ProductId,i.MinimumQuantity,i.Amount,i.CurrencyCode
            FROM dbo.PriceListItems i
            JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId
            WHERE l.BusinessId=@BusinessId AND l.IsActive=1 AND i.IsActive=1
              AND i.ValidFrom<=SYSDATETIMEOFFSET()
              AND (i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET());

            SELECT i.PriceChannelId,i.ProductId,i.Amount,i.CurrencyCode,
              CONVERT(bit,CASE WHEN e.ProductId IS NULL THEN 0 ELSE 1 END)
            FROM dbo.ResolvedPriceChannelItems i
            JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId
            LEFT JOIN dbo.PriceChannelExclusions e
              ON e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId
            WHERE c.BusinessId=@BusinessId AND c.IsActive=1 AND i.IsActive=1
              AND i.ValidFrom<=SYSDATETIMEOFFSET()
              AND (i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET());

            SELECT c.CustomerId,
              COALESCE(p.Identification,N''),
              COALESCE(p.DisplayName,p.LegalName,p.Identification,N''),
              s.PriceListId,s.PriceChannelId,c.IsActive
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId AND p.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            WHERE c.BusinessId=@BusinessId AND p.IsActive=1;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@DeviceId", deviceId),
            new SqlParameter("@TenantId", tenantId),
            new SqlParameter("@BusinessId", businessId)
        ]);
        var lists = new List<PosPriceListItem>();
        var channels = new List<PosPriceChannelItem>();
        var customers = new List<PosCustomerPricing>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lists.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            channels.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetString(3), reader.GetBoolean(4)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            customers.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetBoolean(5)));
        return new PosPricingSnapshot(lists, channels, customers);
    }
}
