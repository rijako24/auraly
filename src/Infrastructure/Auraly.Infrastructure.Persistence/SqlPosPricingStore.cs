using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using System.Text.Json;

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
        command.CommandText = "dbo.PosPricingSnapshotGet";
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.Parameters.AddRange(
        [
            new SqlParameter("@DeviceId", deviceId),
            new SqlParameter("@TenantId", tenantId),
            new SqlParameter("@BusinessId", businessId),
            new SqlParameter("@WarehouseId", warehouseId)
        ]);
        var channels = new List<PosPriceChannelItem>();
        var customers = new List<PosCustomerPricing>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            channels.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetString(4), reader.GetBoolean(5)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            customers.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetBoolean(5), reader.GetBoolean(4), reader.GetBoolean(6),
                JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        var rules = new List<PosWithholdingRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            rules.Add(new(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetDecimal(10),
                reader.GetDecimal(11), JsonSerializer.Deserialize<string[]>(reader.GetString(12)) ?? [],
                DateOnly.FromDateTime(reader.GetDateTime(13)),
                reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14)),
                reader.GetBoolean(15)));
        return new PosPricingSnapshot(channels, customers, rules);
    }
}
