using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryQueryStore(SqlServerConnectionFactory connections, Auraly.BuildingBlocks.Domain.Identifiers.IAuralyIdGenerator ids) : IInventoryQueryStore
{
    public async Task<InventoryProductPage> GetProductsAsync(InventoryUserIdentity user, InventoryProductQuery query, bool includeCosts, CancellationToken token)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND IsActive=1)
              THROW 51201,'The warehouse is outside the authenticated business.',1;
            SELECT COUNT(*) FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products root ON root.BusinessId=p.BusinessId AND root.ProductId=link.ParentProductId
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND COALESCE(root.ManageStock,p.ManageStock)=1
              AND link.ProductLinkId IS NULL
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern OR p.Name LIKE @Pattern OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));
            SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,COALESCE(p.BaseUnitCode,N'UN'),
                   COALESCE(b.QuantityOnHand,0) / COALESCE(NULLIF(link.InventoryFactor,0),1),
                   CASE WHEN @IncludeCosts=1
                        THEN COALESCE(b.AverageUnitCost,0) * COALESCE(NULLIF(link.InventoryFactor,0),1)
                   END
            FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products root ON root.BusinessId=p.BusinessId AND root.ProductId=link.ParentProductId
            LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=p.BusinessId AND b.ProductId=COALESCE(link.ParentProductId,p.ProductId) AND b.WarehouseId=@WarehouseId
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND COALESCE(root.ManageStock,p.ManageStock)=1
              AND link.ProductLinkId IS NULL
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern OR p.Name LIKE @Pattern OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
            ORDER BY p.Name,p.ProductId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection);
        AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryProductItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.IsDBNull(6)?null:reader.GetDecimal(6)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }
    public async Task<IReadOnlyList<InventoryWarehouseOption>> GetWarehousesAsync(InventoryUserIdentity user, CancellationToken token)
    {
        const string sql = "SELECT WarehouseId,Code,Name FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND IsActive=1 ORDER BY Name;";
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token); var items=new List<InventoryWarehouseOption>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2))); return items;
    }

    public async Task<IReadOnlyList<WarehouseMasterItem>> GetWarehouseMastersAsync(InventoryUserIdentity user, CancellationToken token)
    {
        const string sql = "SELECT WarehouseId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsActive FROM dbo.Warehouses WHERE BusinessId=@BusinessId ORDER BY Name;";
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token); var items=new List<WarehouseMasterItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetString(4),reader.GetBoolean(5))); return items;
    }

    public async Task<WarehouseMasterItem> SaveWarehouseAsync(InventoryUserIdentity user, Guid? warehouseId, SaveWarehouseRequest request, CancellationToken token)
    {
        var id=warehouseId??ids.NewId(); var code=$"BOD-{id.ToString("N")[^12..].ToUpperInvariant()}";
        const string sql="""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51201,'The business is outside the authenticated tenant.',1;
            IF @IsNew=1
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsActive,CreatedAt)
              VALUES(@WarehouseId,@BusinessId,@Code,@Name,@AllowNegative,@CostBasis,@IsActive,SYSUTCDATETIME());
            ELSE
            BEGIN
              UPDATE dbo.Warehouses SET Name=@Name,AllowNegativeStockSales=@AllowNegative,PriceFormationCostBasis=@CostBasis,IsActive=@IsActive
              WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId;
              IF @@ROWCOUNT=0 THROW 51201,'Warehouse was not found in the authenticated business.',1;
            END;
            SELECT WarehouseId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsActive FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId;
            """;
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@WarehouseId",id);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@Code",code);command.Parameters.AddWithValue("@Name",request.Name);command.Parameters.AddWithValue("@AllowNegative",request.AllowNegativeStockSales);command.Parameters.AddWithValue("@CostBasis",request.PriceFormationCostBasis);command.Parameters.AddWithValue("@IsActive",request.IsActive);command.Parameters.AddWithValue("@IsNew",warehouseId is null);
        await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);return new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetString(4),reader.GetBoolean(5));
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
        const string sql = """
            SELECT COUNT(*) FROM dbo.InventoryBalances b
            INNER JOIN dbo.Products p ON p.ProductId=b.ProductId AND p.BusinessId=b.BusinessId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.BusinessId=b.BusinessId
            WHERE b.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId)
              AND (@ProductId IS NULL OR b.ProductId=@ProductId)
              AND (@OnlyWithStock=0 OR b.QuantityOnHand<>0)
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern);
            SELECT b.WarehouseId,w.Code,w.Name,b.ProductId,COALESCE(p.ProductCode,N''),p.Name,b.QuantityOnHand,
                   CASE WHEN @IncludeCosts=1 THEN b.AverageUnitCost END,
                   CASE WHEN @IncludeCosts=1 THEN b.InventoryValue END,b.UpdatedAt
            FROM dbo.InventoryBalances b
            INNER JOIN dbo.Products p ON p.ProductId=b.ProductId AND p.BusinessId=b.BusinessId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.BusinessId=b.BusinessId
            WHERE b.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId)
              AND (@ProductId IS NULL OR b.ProductId=@ProductId)
              AND (@OnlyWithStock=0 OR b.QuantityOnHand<>0)
              AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern)
            ORDER BY p.Name,b.WarehouseId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection);
        AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@ProductId",(object?)query.ProductId??DBNull.Value); command.Parameters.AddWithValue("@OnlyWithStock",query.OnlyWithStock); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryBalanceItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetDecimal(8),reader.IsDBNull(9)?null:reader.GetFieldValue<DateTimeOffset>(9)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    public async Task<InventoryMovementPage> GetMovementsAsync(InventoryUserIdentity user, InventoryMovementQuery query, bool includeCosts, CancellationToken token)
    {
        const string filter="""m.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR m.WarehouseId=@WarehouseId) AND (@ProductId IS NULL OR m.ProductId=@ProductId) AND (@DocumentType IS NULL OR m.DocumentType=@DocumentType) AND (@MovementType IS NULL OR m.MovementType=@MovementType) AND (@From IS NULL OR m.OccurredAt>=@From) AND (@To IS NULL OR m.OccurredAt<@To) AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Name LIKE @Pattern OR o.DocumentNumber LIKE @Pattern)""";
        var sql=$"""
            SELECT COUNT(*) FROM dbo.InventoryMovements m INNER JOIN dbo.Products p ON p.ProductId=m.ProductId LEFT JOIN dbo.InventoryOperations o ON o.InventoryOperationId=m.DocumentId WHERE {filter};
            SELECT m.InventoryMovementId,m.WarehouseId,w.Name,m.ProductId,COALESCE(p.ProductCode,N''),p.Name,m.DocumentId,m.DocumentType,o.DocumentNumber,m.MovementType,m.QuantityChange,COALESCE(m.QuantityBefore,0),COALESCE(m.QuantityAfter,0),CASE WHEN @IncludeCosts=1 THEN m.RecognizedUnitCost END,CASE WHEN @IncludeCosts=1 THEN m.ValueChange END,m.OccurredAt,m.PostedAt
            FROM dbo.InventoryMovements m INNER JOIN dbo.Products p ON p.ProductId=m.ProductId INNER JOIN dbo.Warehouses w ON w.WarehouseId=m.WarehouseId LEFT JOIN dbo.InventoryOperations o ON o.InventoryOperationId=m.DocumentId WHERE {filter}
            ORDER BY m.ProcessingSequence DESC,m.LineNumber OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@ProductId",(object?)query.ProductId??DBNull.Value); command.Parameters.AddWithValue("@DocumentType",(object?)query.DocumentType??DBNull.Value); command.Parameters.AddWithValue("@MovementType",(object?)query.MovementType??DBNull.Value); command.Parameters.AddWithValue("@From",(object?)query.From??DBNull.Value); command.Parameters.AddWithValue("@To",(object?)query.To??DBNull.Value); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryMovementItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetGuid(6),reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.GetString(9),reader.GetDecimal(10),reader.GetDecimal(11),reader.GetDecimal(12),reader.IsDBNull(13)?null:reader.GetDecimal(13),reader.IsDBNull(14)?null:reader.GetDecimal(14),reader.GetFieldValue<DateTimeOffset>(15),reader.IsDBNull(16)?null:reader.GetFieldValue<DateTimeOffset>(16)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    public async Task<InventoryOperationPage> GetOperationsAsync(InventoryUserIdentity user, InventoryOperationQuery query, bool includeCosts, CancellationToken token)
    {
        const string filter="""BusinessId=@BusinessId AND (@WarehouseId IS NULL OR WarehouseId=@WarehouseId) AND (@DocumentType IS NULL OR DocumentType=@DocumentType) AND (@Status IS NULL OR Status=@Status) AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To) AND (@Search IS NULL OR SearchText LIKE @Pattern)""";
        var sql=$"""
            SELECT
              o.InventoryOperationId DocumentId,o.DocumentType,o.DocumentNumber,o.BusinessId,
              o.WarehouseId,w.Name WarehouseName,o.DestinationWarehouseId,
              dw.Name DestinationWarehouseName,o.ReasonDescription ReasonCode,o.Status,o.OccurredAt,
              (SELECT COUNT(*) FROM dbo.InventoryOperationLines l
               WHERE l.InventoryOperationId=o.InventoryOperationId) LineCount,
              o.TotalValueChange,
              CONCAT(o.DocumentNumber,N' ',o.ReasonDescription,N' ',o.ReasonCode,N' ',w.Name) SearchText
            INTO #InventoryHistory
            FROM dbo.InventoryOperations o
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId
            LEFT JOIN dbo.Warehouses dw ON dw.WarehouseId=o.DestinationWarehouseId
            WHERE o.BusinessId=@BusinessId
            UNION ALL
            SELECT
              g.GoodsReceiptId,N'GoodsReceipt',g.DocumentNumber,g.BusinessId,
              g.WarehouseId,w.Name,NULL,NULL,N'GOODS_RECEIPT',g.Status,g.ReceivedAt,
              (SELECT COUNT(*) FROM dbo.GoodsReceiptLines l
               WHERE l.GoodsReceiptId=g.GoodsReceiptId),
              COALESCE((SELECT SUM(m.ValueChange) FROM dbo.InventoryMovements m
                        WHERE m.DocumentId=g.GoodsReceiptId),0),
              CONCAT(g.DocumentNumber,N' ',g.SupplierInvoiceNumber,N' ',w.Name,N' ',s.DisplayName)
            FROM dbo.GoodsReceipts g
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=g.WarehouseId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=g.SupplierId
            INNER JOIN dbo.Parties s ON s.PartyId=supplier.PartyId
            WHERE g.BusinessId=@BusinessId;

            SELECT COUNT(*) FROM #InventoryHistory WHERE {filter};
            SELECT DocumentId,DocumentType,DocumentNumber,WarehouseId,WarehouseName,
              DestinationWarehouseId,DestinationWarehouseName,ReasonCode,Status,OccurredAt,
              LineCount,CASE WHEN @IncludeCosts=1 THEN TotalValueChange END
            FROM #InventoryHistory WHERE {filter}
            ORDER BY OccurredAt DESC,DocumentId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            DROP TABLE #InventoryHistory;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(token); await using var command=new SqlCommand(sql,connection); AddCommon(command,user.BusinessId,query.WarehouseId,query.Search,query.Page,query.PageSize); command.Parameters.AddWithValue("@DocumentType",(object?)query.DocumentType??DBNull.Value); command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value); command.Parameters.AddWithValue("@From",(object?)query.From??DBNull.Value); command.Parameters.AddWithValue("@To",(object?)query.To??DBNull.Value); command.Parameters.AddWithValue("@IncludeCosts",includeCosts);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total=reader.GetInt32(0); await reader.NextResultAsync(token); var items=new List<InventoryOperationItem>(); while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetGuid(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetFieldValue<DateTimeOffset>(9),reader.GetInt32(10),reader.IsDBNull(11)?null:reader.GetDecimal(11)));
        return new(items,query.Page,query.PageSize,total,Pages(total,query.PageSize));
    }

    private static void AddCommon(SqlCommand command,Guid businessId,Guid? warehouseId,string? search,int page,int pageSize){command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@WarehouseId",(object?)warehouseId??DBNull.Value);command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value);command.Parameters.AddWithValue("@Pattern",search is null?DBNull.Value:$"%{search}%");command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@PageSize",pageSize);}
    private static int Pages(int total,int pageSize)=>total==0?0:(int)Math.Ceiling(total/(double)pageSize);
}