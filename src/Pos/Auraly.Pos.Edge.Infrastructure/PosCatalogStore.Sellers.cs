using System.Text.Json;
using Auraly.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task<(long? Cursor,bool Bootstrapping,string? Next)> SellerSynchronizationStateAsync(CancellationToken ct=default)
    {
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT SellerCursor,SellerBootstrapActive,SellerBootstrapNextCursor FROM PosPricingSynchronizationState WHERE StateId=1;";
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);
        return(reader.IsDBNull(0)?null:reader.GetInt64(0),reader.GetInt64(1)==1,reader.IsDBNull(2)?null:reader.GetString(2));
    }

    public async Task BeginSellerBootstrapAsync(CancellationToken ct=default)
    {
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await ExecutePricingAsync(connection,transaction,"""
            DELETE FROM PosSellerStaging;
            UPDATE PosPricingSynchronizationState SET SellerCursor=NULL,SellerBootstrapActive=1,
              SellerBootstrapNextCursor=NULL,SellerBootstrapThroughCursor=NULL WHERE StateId=1;
            """,[],ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ApplySellerBootstrapPageAsync(PosSellerBootstrapPage page,CancellationToken ct=default)
    {
        if(page.ThroughCursor<0 || page.HasMore&&string.IsNullOrWhiteSpace(page.NextCursor))
            throw new InvalidDataException("La página de vendedores no conserva su posición de descarga.");
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        var finish=page.HasMore?"":"""
            DELETE FROM PosSellers;
            INSERT INTO PosSellers SELECT * FROM PosSellerStaging;
            DELETE FROM PosSellerStaging;
            UPDATE PosPricingSynchronizationState SET SellerCursor=SellerBootstrapThroughCursor,SellerBootstrapActive=0,
              SellerBootstrapNextCursor=NULL,SellerBootstrapThroughCursor=NULL WHERE StateId=1;
            """;
        await ExecutePricingAsync(connection,transaction,"""
            INSERT OR REPLACE INTO PosSellerStaging(SellerId,PartyId,Code,Name,Identification,IsActive)
            SELECT json_extract(value,'$.SellerId'),json_extract(value,'$.PartyId'),json_extract(value,'$.Code'),
              json_extract(value,'$.Name'),json_extract(value,'$.Identification'),json_extract(value,'$.IsActive')
            FROM json_each(@Rows);
            UPDATE PosPricingSynchronizationState SET SellerBootstrapThroughCursor=COALESCE(SellerBootstrapThroughCursor,@Through),
              SellerBootstrapNextCursor=@Next WHERE StateId=1;
            """+finish,[Q("@Rows",JsonSerializer.Serialize(page.Sellers)),Q("@Through",page.ThroughCursor),Q("@Next",page.NextCursor)],ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ApplySellerChangesAsync(PosSellerDeltaPage page,CancellationToken ct=default)
    {
        if(page.FromCursor<0 || page.ToCursor<page.FromCursor || page.HasMore&&page.ToCursor==page.FromCursor || page.Changes.Any(row=>row.Version<=page.FromCursor||row.Version>page.ToCursor))
            throw new InvalidDataException("La posición del cambio de vendedores no es válida.");
        // A page can carry several notifications for the same role. Apply its last
        // version, including a tombstone, once; never resurrect an earlier value.
        var changes=page.Changes.GroupBy(row=>row.SellerId).Select(group=>group.MaxBy(row=>row.Version)!).ToArray();
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await using(var guard=connection.CreateCommand())
        {
            guard.Transaction=(SqliteTransaction)transaction;
            guard.CommandText="SELECT SellerCursor FROM PosPricingSynchronizationState WHERE StateId=1;";
            if(await guard.ExecuteScalarAsync(ct) is not long current || current!=page.FromCursor)
                throw new InvalidOperationException("Cambió la posición local de vendedores. Actualiza los datos de la caja.");
        }
        await ExecutePricingAsync(connection,transaction,"""
            UPDATE PosSellers SET IsActive=0 WHERE SellerId IN(
              SELECT json_extract(value,'$.SellerId') FROM json_each(@Rows) WHERE json_type(value,'$.Seller')='null');
            INSERT OR REPLACE INTO PosSellers(SellerId,PartyId,Code,Name,Identification,IsActive)
            SELECT json_extract(value,'$.Seller.SellerId'),json_extract(value,'$.Seller.PartyId'),json_extract(value,'$.Seller.Code'),
              json_extract(value,'$.Seller.Name'),json_extract(value,'$.Seller.Identification'),json_extract(value,'$.Seller.IsActive')
            FROM json_each(@Rows) WHERE json_type(value,'$.Seller')<>'null';
            UPDATE PosPricingSynchronizationState SET SellerCursor=@To WHERE StateId=1;
            """,[Q("@Rows",JsonSerializer.Serialize(changes)),Q("@To",page.ToCursor)],ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<PosSellerPage> SearchSellersAsync(string? search,int skip=0,int take=20,CancellationToken ct=default)
    {
        if(skip<0||take is <1 or >100||skip>int.MaxValue-take)throw new ArgumentOutOfRangeException(nameof(take));
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT CASE WHEN SellerCursor IS NOT NULL OR EXISTS(SELECT 1 FROM PosSellers) THEN 1 ELSE 0 END
              FROM PosPricingSynchronizationState WHERE StateId=1;
            SELECT SellerId,PartyId,Code,Name,Identification,IsActive FROM PosSellers WHERE IsActive=1
              AND (@Search='' OR Name LIKE @Pattern ESCAPE '~' OR Code LIKE @Pattern ESCAPE '~' OR Identification LIKE @Pattern ESCAPE '~')
            ORDER BY Name COLLATE NOCASE,SellerId LIMIT @Take OFFSET @Skip;
            """;
        var term=search?.Trim()??"";
        command.Parameters.AddRange([Q("@Search",term),Q("@Pattern","%"+term.Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%"),Q("@Skip",skip),Q("@Take",take+1)]);
        var rows=new List<PosSeller>();await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)||reader.GetInt64(0)!=1)
            throw new InvalidOperationException("Los vendedores todavía no están preparados en esta caja. Actualiza los datos e intenta de nuevo.");
        await reader.NextResultAsync(ct);
        while(await reader.ReadAsync(ct))rows.Add(ReadLocalSeller(reader));
        var more=rows.Count>take;if(more)rows.RemoveAt(rows.Count-1);
        return new(rows,more,more?skip+take:null);
    }

    public async Task<PosSeller?> GetSellerAsync(Guid id,CancellationToken ct=default)
    {
        await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();command.CommandText="SELECT SellerId,PartyId,Code,Name,Identification,IsActive FROM PosSellers WHERE SellerId=@Id;";
        command.Parameters.Add(Q("@Id",id));await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)?ReadLocalSeller(reader):null;
    }

    private static PosSeller ReadLocalSeller(SqliteDataReader reader)=>new(Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),
        reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetInt64(5)==1);
}
