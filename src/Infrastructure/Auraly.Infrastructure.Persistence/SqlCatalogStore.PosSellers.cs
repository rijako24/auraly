using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<PosSellerBootstrapPage> SellerBootstrapPageAsync(Guid deviceId, Guid tenantId,
        Guid businessId, string? cursor, int pageSize, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await EnsurePosPartyScopeAsync(connection, deviceId, tenantId, businessId, ct);
        await using var command = new SqlCommand("""
            SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages
              WHERE BusinessId=@Business AND Stream=N'Sellers';
            SELECT TOP(@Take) seller.SellerId,seller.PartyId,seller.Code,party.DisplayName,party.Identification,
              CAST(1 AS bit) IsActive
            FROM dbo.CommerceSellers seller JOIN dbo.Parties party ON party.PartyId=seller.PartyId AND party.TenantId=seller.TenantId
            WHERE seller.TenantId=@Tenant AND seller.IsActive=1 AND party.IsActive=1
              AND (@After IS NULL OR seller.SellerId>@After)
            ORDER BY seller.SellerId;
            """, connection);
        command.Parameters.AddRange([P("@Business",businessId),P("@Tenant",tenantId),P("@Take",pageSize+1),
            P("@After",cursor is null?null:Guid.Parse(cursor))]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct); var through=reader.GetInt64(0);
        await reader.NextResultAsync(ct);
        var rows=new List<PosSeller>();
        while(await reader.ReadAsync(ct)) rows.Add(ReadPosSeller(reader,0));
        var more=rows.Count>pageSize; if(more) rows.RemoveAt(rows.Count-1);
        return new(through,more?rows[^1].SellerId.ToString("D"):null,more,rows);
    }

    public async Task<PosSellerDeltaPage> SellerChangesAsync(Guid deviceId, Guid tenantId,
        Guid businessId, long cursor, int pageSize, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await EnsurePosPartyScopeAsync(connection,deviceId,tenantId,businessId,ct);
        await using var command=new SqlCommand("""
            WITH page AS(SELECT TOP(@Take) AvailableThroughCursor,EntityId FROM dbo.PosSynchronizationOutboxMessages
              WHERE BusinessId=@Business AND Stream=N'Sellers' AND EntityType=N'Seller' AND AvailableThroughCursor>@Cursor
              ORDER BY AvailableThroughCursor)
            SELECT page.AvailableThroughCursor,page.EntityId,seller.SellerId,seller.PartyId,seller.Code,
              party.DisplayName,party.Identification,CAST(CASE WHEN seller.IsActive=1 AND party.IsActive=1 THEN 1 ELSE 0 END AS bit)
            FROM page LEFT JOIN dbo.CommerceSellers seller ON seller.SellerId=page.EntityId AND seller.TenantId=@Tenant
              LEFT JOIN dbo.Parties party ON party.PartyId=seller.PartyId AND party.TenantId=@Tenant
            ORDER BY page.AvailableThroughCursor;
            """,connection);
        command.Parameters.AddRange([P("@Business",businessId),P("@Tenant",tenantId),P("@Take",pageSize+1),P("@Cursor",cursor)]);
        await using var reader=await command.ExecuteReaderAsync(ct);
        var rows=new List<PosSellerDelta>();
        while(await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt64(0),reader.GetGuid(1),
            reader.IsDBNull(2)||!reader.GetBoolean(7)?null:ReadPosSeller(reader,2)));
        var more=rows.Count>pageSize; if(more) rows.RemoveAt(rows.Count-1);
        return new(cursor,rows.Count==0?cursor:rows[^1].Version,more,rows);
    }

    private static PosSeller ReadPosSeller(SqlDataReader reader,int offset) => new(reader.GetGuid(offset),reader.GetGuid(offset+1),
        reader.GetString(offset+2),reader.GetString(offset+3),reader.IsDBNull(offset+4)?null:reader.GetString(offset+4),reader.GetBoolean(offset+5));
}
