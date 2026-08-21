using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryQueryStore(SqlServerConnectionFactory connections, Auraly.BuildingBlocks.Domain.Identifiers.IAuralyIdGenerator ids) : IInventoryQueryStore
{
    public async Task<InventoryProductPage> GetProductsAsync(InventoryUserIdentity user, InventoryProductQuery query, bool includeCosts, CancellationToken token)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND IsActive=1 AND UseForSales=1)
              THROW 51201,'La bodega no está habilitada como bodega de venta.',1;
            SELECT COUNT(*) FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products root ON root.BusinessId=p.BusinessId AND root.ProductId=link.ParentProductId
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND COALESCE(root.ManageStock,p.ManageStock)=1
              AND link.ProductLinkId IS NULL
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern OR p.Name LIKE @Pattern OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));
            SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,COALESCE(p.BaseUnitCode,N'UN'),
                   COALESCE(b.QuantityOnHand,0) / COALESCE(NULLIF(link.InventoryFactor,0),1),
                   CASE WHEN @IncludeCosts=1
                        THEN COALESCE(NULLIF(b.AverageUnitCost,0),price.CostBasisAmount,0) * COALESCE(NULLIF(link.InventoryFactor,0),1)
                   END,
                   price.Amount * COALESCE(NULLIF(link.PriceFactor,0),1)
            FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products root ON root.BusinessId=p.BusinessId AND root.ProductId=link.ParentProductId
            LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=p.BusinessId AND b.ProductId=COALESCE(link.ParentProductId,p.ProductId) AND b.WarehouseId=@WarehouseId
            LEFT JOIN dbo.ProductPrices price ON price.BusinessId=p.BusinessId AND price.ProductId=p.ProductId
              AND price.IsActive=1 AND price.ValidFrom<=SYSUTCDATETIME() AND (price.ValidUntil IS NULL OR price.ValidUntil>SYSUTCDATETIME())
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND COALESCE(root.ManageStock,p.ManageStock)=1
              AND link.ProductLinkId IS NULL
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern OR p.Name LIKE @Pattern OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
            ORDER BY p.Name,p.ProductId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection);
        AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryProductItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.IsDBNull(6)?null:reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetDecimal(7)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }
    public async Task<IReadOnlyList<InventoryWarehouseOption>> GetWarehousesAsync(InventoryUserIdentity user, CancellationToken token)
    {
        const string sql = "SELECT WarehouseId,Code,Name FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1 ORDER BY Name;";
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token); var items=new List<InventoryWarehouseOption>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2))); return items;
    }

    public async Task<IReadOnlyList<WarehouseMasterItem>> GetWarehouseMastersAsync(InventoryUserIdentity user, CancellationToken token)
    {
        const string sql = "SELECT WarehouseId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive FROM dbo.Warehouses WHERE BusinessId=@BusinessId ORDER BY IsSystem,Name;";
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token); var items=new List<WarehouseMasterItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetBoolean(7),reader.GetBoolean(8),reader.GetBoolean(9))); return items;
    }

    public async Task<WarehouseMasterItem> SaveWarehouseAsync(InventoryUserIdentity user, Guid? warehouseId, SaveWarehouseRequest request, CancellationToken token)
    {
        var id=warehouseId??ids.NewId(); var code=$"BOD-{id.ToString("N")[^12..].ToUpperInvariant()}";
        const string sql="""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51201,'The business is outside the authenticated tenant.',1;
            IF @IsNew=1
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(@WarehouseId,@BusinessId,@Code,@Name,@AllowNegative,@CostBasis,0,@UseForSales,@UseForSales,@UseForSales,@IsActive,SYSUTCDATETIME());
            ELSE
            BEGIN
              UPDATE dbo.Warehouses SET Name=@Name,AllowNegativeStockSales=@AllowNegative,PriceFormationCostBasis=@CostBasis,
                UseForSales=@UseForSales,UseForGoodsReceipts=@UseForSales,IsInventoryVisible=@UseForSales,IsActive=@IsActive
              WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsSystem=0;
              IF @@ROWCOUNT=0 THROW 51201,'Warehouse was not found in the authenticated business.',1;
            END;
            SELECT WarehouseId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId;
            """;
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@WarehouseId",id);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@Code",code);command.Parameters.AddWithValue("@Name",request.Name);command.Parameters.AddWithValue("@AllowNegative",request.AllowNegativeStockSales);command.Parameters.AddWithValue("@CostBasis",request.PriceFormationCostBasis);command.Parameters.AddWithValue("@UseForSales",request.UseForSales);command.Parameters.AddWithValue("@IsActive",request.IsActive);command.Parameters.AddWithValue("@IsNew",warehouseId is null);
        await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);return new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetBoolean(7),reader.GetBoolean(8),reader.GetBoolean(9));
    }
    public async Task<IReadOnlyList<InventoryReasonItem>> GetReasonsAsync(InventoryUserIdentity user, string? operationType, bool includeInactive, string? search, CancellationToken token)
    {
        const string sql = """
            SELECT InventoryReasonId,OperationType,Code,Name,IsSystem,IsActive,DisplayOrder
            FROM dbo.InventoryReasons
            WHERE BusinessId=@BusinessId AND (@OperationType IS NULL OR OperationType=@OperationType)
              AND (@IncludeInactive=1 OR IsActive=1)
              AND (@Search IS NULL OR Name LIKE @Pattern OR Code LIKE @Pattern)
            ORDER BY OperationType,DisplayOrder,Name;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@OperationType",(object?)operationType??DBNull.Value); command.Parameters.AddWithValue("@IncludeInactive",includeInactive); command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value); command.Parameters.AddWithValue("@Pattern",search is null?DBNull.Value:$"%{search}%");
        await using var reader=await command.ExecuteReaderAsync(token); var items=new List<InventoryReasonItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetBoolean(4),reader.GetBoolean(5),reader.GetInt32(6)));
        return items;
    }

    public async Task<InventoryReasonItem> SaveReasonAsync(InventoryUserIdentity user, Guid? inventoryReasonId, SaveInventoryReasonRequest request, CancellationToken token)
    {
        var id=inventoryReasonId??ids.NewId(); var code=$"MOT-{id.ToString("N")[^12..].ToUpperInvariant()}";
        const string sql="""
            IF @IsNew=1
              INSERT dbo.InventoryReasons(InventoryReasonId,BusinessId,OperationType,Code,Name,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
              VALUES(@Id,@BusinessId,@OperationType,@Code,@Name,0,@IsActive,@DisplayOrder,SYSUTCDATETIME(),SYSUTCDATETIME());
            ELSE
            BEGIN
              UPDATE dbo.InventoryReasons SET OperationType=@OperationType,Name=@Name,IsActive=@IsActive,DisplayOrder=@DisplayOrder,UpdatedAt=SYSUTCDATETIME()
              WHERE InventoryReasonId=@Id AND BusinessId=@BusinessId;
              IF @@ROWCOUNT=0 THROW 51220,'Inventory reason was not found in the authenticated business.',1;
            END;
            SELECT InventoryReasonId,OperationType,Code,Name,IsSystem,IsActive,DisplayOrder FROM dbo.InventoryReasons WHERE InventoryReasonId=@Id;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@Id",id); command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@OperationType",request.OperationType); command.Parameters.AddWithValue("@Code",code); command.Parameters.AddWithValue("@Name",request.Name); command.Parameters.AddWithValue("@IsActive",request.IsActive); command.Parameters.AddWithValue("@DisplayOrder",request.DisplayOrder); command.Parameters.AddWithValue("@IsNew",inventoryReasonId is null);
        try { await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); return new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetBoolean(4),reader.GetBoolean(5),reader.GetInt32(6)); }
        catch(SqlException exception) when(exception.Number is 2601 or 2627) { throw new InventoryConflictException("An inventory reason with the same name already exists for this operation."); }
    }

    public async Task<InventoryBalancePage> GetBalancesAsync(InventoryUserIdentity user, InventoryBalanceQuery query, bool includeCosts, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand("dbo.InventoryBalancesSearch",connection){CommandType=CommandType.StoredProcedure};
        AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@ProductId",(object?)query.ProductId??DBNull.Value); command.Parameters.AddWithValue("@OnlyWithStock",query.OnlyWithStock); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryBalanceItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetDecimal(8),reader.IsDBNull(9)?null:reader.GetFieldValue<DateTimeOffset>(9)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    public async Task<InventoryMovementPage> GetMovementsAsync(InventoryUserIdentity user, InventoryMovementQuery query, bool includeCosts, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand("dbo.InventoryMovementsSearch",connection){CommandType=CommandType.StoredProcedure}; AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@ProductId",(object?)query.ProductId??DBNull.Value); command.Parameters.AddWithValue("@DocumentType",(object?)query.DocumentType??DBNull.Value); command.Parameters.AddWithValue("@MovementType",(object?)query.MovementType??DBNull.Value); command.Parameters.AddWithValue("@From",(object?)query.From??DBNull.Value); command.Parameters.AddWithValue("@To",(object?)query.To??DBNull.Value); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryMovementItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetGuid(6),reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.GetString(9),reader.GetDecimal(10),reader.GetDecimal(11),reader.GetDecimal(12),reader.IsDBNull(13)?null:reader.GetDecimal(13),reader.IsDBNull(14)?null:reader.GetDecimal(14),reader.GetFieldValue<DateTimeOffset>(15),reader.IsDBNull(16)?null:reader.GetFieldValue<DateTimeOffset>(16)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    public async Task<InventoryOperationPage> GetOperationsAsync(InventoryUserIdentity user, InventoryOperationQuery query, bool includeCosts, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand("dbo.InventoryOperationsSearch",connection){CommandType=CommandType.StoredProcedure}; AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@DocumentType",(object?)query.DocumentType??DBNull.Value); command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value); command.Parameters.AddWithValue("@From",(object?)query.From??DBNull.Value); command.Parameters.AddWithValue("@To",(object?)query.To??DBNull.Value); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryOperationItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetGuid(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetFieldValue<DateTimeOffset>(9),reader.GetInt32(10),reader.IsDBNull(11)?null:reader.GetDecimal(11)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    public async Task<InventoryOperationDetail?> GetOperationDetailAsync(
        InventoryUserIdentity user, Guid documentId, bool includeCosts, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        const string sql = """
            SELECT TOP(1) d.DocumentType,d.DocumentNumber,d.WarehouseId,d.WarehouseName,
                   d.DestinationWarehouseId,d.DestinationWarehouseName,d.ReasonCode,
                   d.ReasonDescription,d.ConversionType,d.BaseInventorySequence,d.Notes,
                   d.Status,d.OccurredAt,d.CreatedAt,d.AcceptedAt,d.ProcessedAt,
                   CASE WHEN @IncludeCosts=1 THEN d.TotalValueChange END
            FROM (
              SELECT o.InventoryOperationId DocumentId,o.DocumentType,o.DocumentNumber,
                     o.WarehouseId,w.Name WarehouseName,o.DestinationWarehouseId,
                     dw.Name DestinationWarehouseName,o.ReasonCode,o.ReasonDescription,
                     o.ConversionType,o.BaseInventorySequence,o.Notes,o.Status,o.OccurredAt,
                     o.CreatedAt,o.AcceptedAt,o.ProcessedAt,o.TotalValueChange
              FROM dbo.InventoryOperations o
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.UseForSales=1
              LEFT JOIN dbo.Warehouses dw ON dw.WarehouseId=o.DestinationWarehouseId
              WHERE o.BusinessId=@BusinessId
              UNION ALL
              SELECT g.GoodsReceiptId,N'GoodsReceipt',g.DocumentNumber,g.WarehouseId,w.Name,
                     NULL,NULL,N'GOODS_RECEIPT',N'Recepción de mercancía',NULL,NULL,g.Notes,
                     g.Status,g.ReceivedAt,g.AcceptedAt,g.AcceptedAt,g.ProcessedAt,
                     COALESCE((SELECT SUM(m.ValueChange) FROM dbo.InventoryMovements m
                               WHERE m.DocumentId=g.GoodsReceiptId),0)
              FROM dbo.GoodsReceipts g
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=g.WarehouseId AND w.UseForSales=1
              WHERE g.BusinessId=@BusinessId
            ) d
            WHERE d.DocumentId=@DocumentId;

            SELECT l.LineNumber,l.Direction,l.ProductId,l.ProductCode,l.ProductName,
                   l.Quantity,l.PreCountQuantity,l.SystemQuantityAtBase,
                   CASE WHEN @IncludeCosts=1 THEN l.ExplicitUnitCost END,
                   l.AllocationWeight,
                   CASE WHEN @IncludeCosts=1 THEN l.ProcessedUnitCost END,
                   CASE WHEN @IncludeCosts=1 THEN l.ProcessedValue END
            FROM (
              SELECT l.InventoryOperationId DocumentId,l.LineNumber,l.Direction,l.ProductId,
                     l.ProductCodeSnapshot ProductCode,l.DescriptionSnapshot ProductName,
                     l.Quantity,l.PreCountQuantity,l.SystemQuantityAtBase,l.ExplicitUnitCost,l.AllocationWeight,
                     l.ProcessedUnitCost,l.ProcessedValue
              FROM dbo.InventoryOperationLines l
              UNION ALL
              SELECT l.GoodsReceiptId,l.LineNumber,N'RECEIPT',l.ProductId,
                     COALESCE(p.ProductCode,p.Sku),l.DescriptionSnapshot,l.Quantity,
                     NULL,NULL,l.UnitCost,NULL,l.UnitCost,l.LineTotal
              FROM dbo.GoodsReceiptLines l
              INNER JOIN dbo.Products p ON p.ProductId=l.ProductId
            ) l
            WHERE l.DocumentId=@DocumentId
            ORDER BY l.LineNumber;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@IncludeCosts", includeCosts);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;

        var detail = new InventoryOperationDetail(
            documentId, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11),
            reader.GetDateTimeOffset(12), reader.GetDateTimeOffset(13),
            reader.IsDBNull(14) ? null : reader.GetDateTimeOffset(14),
            reader.IsDBNull(15) ? null : reader.GetDateTimeOffset(15),
            reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            Array.Empty<InventoryOperationDetailLine>());

        await reader.NextResultAsync(token);
        var lines = new List<InventoryOperationDetailLine>();
        while (await reader.ReadAsync(token))
            lines.Add(new(
                reader.GetInt32(0), reader.GetString(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11)));
        return detail with { Lines = lines };
    }

    private static void AddCommon(SqlCommand command,Guid businessId,Guid? warehouseId,string? search,int page,int pageSize){command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@WarehouseId",(object?)warehouseId??DBNull.Value);command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value);command.Parameters.AddWithValue("@Pattern",search is null?DBNull.Value:$"%{search}%");command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@PageSize",pageSize);}
    private static int Pages(int total,int pageSize)=>total==0?0:(int)Math.Ceiling(total/(double)pageSize);
}
