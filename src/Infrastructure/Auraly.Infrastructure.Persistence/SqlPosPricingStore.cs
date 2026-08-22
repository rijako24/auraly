using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<PosPricingSnapshot> PricingSnapshotAsync(
        Guid deviceId,
        Guid tenantId,
        Guid businessId,
        Guid warehouseId,
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

            SELECT c.PriceChannelId,p.ProductId,
              CONVERT(decimal(19,4),ROUND(CASE c.Strategy
                WHEN N'PercentageOverBasePrice' THEN basePrice.Amount*(1+COALESCE(c.Value,0)/100)
                WHEN N'PercentageOverAverageCost' THEN cost.Amount*(1+COALESCE(c.Value,0)/100)
                WHEN N'FixedMarginOverAverageCost' THEN cost.Amount/(1-COALESCE(c.Value,0)/100)
                WHEN N'SellAtAverageCost' THEN cost.Amount
                WHEN N'FixedSpecialPrice' THEN special.Amount END,4)),basePrice.CurrencyCode,
              CONVERT(bit,0)
            FROM dbo.PriceChannels c
            JOIN dbo.Products p ON p.BusinessId=c.BusinessId AND p.IsActive=1
            CROSS APPLY(SELECT TOP(1) pp.Amount,pp.CurrencyCode,pp.CostBasisAmount
                        FROM dbo.ProductPrices pp
                        WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1
                          AND pp.ValidFrom<=SYSDATETIMEOFFSET()
                          AND (pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
                        ORDER BY pp.ValidFrom DESC) basePrice
            OUTER APPLY(SELECT COALESCE(MAX(NULLIF(balance.AverageUnitCost,0)),basePrice.CostBasisAmount,0) Amount FROM dbo.InventoryBalances balance WHERE balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=p.ProductId) cost
            OUTER APPLY(SELECT TOP(1) item.Amount FROM dbo.ResolvedPriceChannelItems item WHERE item.PriceChannelId=c.PriceChannelId AND item.ProductId=p.ProductId AND item.IsActive=1 AND item.ValidFrom<=SYSDATETIMEOFFSET() AND(item.ValidUntil IS NULL OR item.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY item.ValidFrom DESC) special
            LEFT JOIN dbo.PriceChannelExclusions exclusion ON exclusion.PriceChannelId=c.PriceChannelId AND exclusion.ProductId=p.ProductId
            WHERE c.BusinessId=@BusinessId AND c.IsActive=1 AND exclusion.ProductId IS NULL
              AND(c.Strategy<>N'FixedSpecialPrice' OR special.Amount IS NOT NULL);

            SELECT c.CustomerId,
              COALESCE(p.NormalizedIdentification,p.Identification,N''),
              COALESCE(p.DisplayName,p.LegalName,p.Identification,N''),
              s.PriceListId,s.PriceChannelId,c.RequiresElectronicInvoice,c.IsActive
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId AND p.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            WHERE c.BusinessId=@BusinessId AND p.IsActive=1;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@DeviceId", deviceId),
            new SqlParameter("@TenantId", tenantId),
            new SqlParameter("@BusinessId", businessId),
            new SqlParameter("@WarehouseId", warehouseId)
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
                reader.GetBoolean(6), reader.GetBoolean(5)));
        return new PosPricingSnapshot(lists, channels, customers);
    }
}
