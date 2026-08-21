CREATE PROCEDURE dbo.InventoryOperationsSearch
    @BusinessId uniqueidentifier,
    @WarehouseId uniqueidentifier = NULL,
    @Search nvarchar(160) = NULL,
    @Pattern nvarchar(324) = NULL,
    @DocumentType nvarchar(80) = NULL,
    @Status nvarchar(80) = NULL,
    @From datetimeoffset = NULL,
    @To datetimeoffset = NULL,
    @IncludeCosts bit,
    @Offset int,
    @PageSize int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.InventoryOperationId DocumentId,o.DocumentType,o.DocumentNumber,o.BusinessId,
           o.WarehouseId,w.Name WarehouseName,o.DestinationWarehouseId,dw.Name DestinationWarehouseName,
           o.ReasonDescription ReasonCode,o.Status,o.OccurredAt,
           (SELECT COUNT(*) FROM dbo.InventoryOperationLines l WHERE l.InventoryOperationId=o.InventoryOperationId) LineCount,
           o.TotalValueChange,
           CONCAT(o.DocumentNumber,N' ',o.ReasonDescription,N' ',o.ReasonCode,N' ',w.Name,N' ',
             (SELECT STRING_AGG(CONCAT(p.ProductCode,N' ',p.Reference,N' ',p.Name),N' ')
              FROM dbo.InventoryOperationLines l INNER JOIN dbo.Products p ON p.ProductId=l.ProductId
              WHERE l.InventoryOperationId=o.InventoryOperationId)) SearchText
    INTO #InventoryHistory
    FROM dbo.InventoryOperations o
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.UseForSales=1
    LEFT JOIN dbo.Warehouses dw ON dw.WarehouseId=o.DestinationWarehouseId
    WHERE o.BusinessId=@BusinessId
    UNION ALL
    SELECT g.GoodsReceiptId,N'GoodsReceipt',g.DocumentNumber,g.BusinessId,g.WarehouseId,w.Name,NULL,NULL,
           N'GOODS_RECEIPT',g.Status,g.ReceivedAt,
           (SELECT COUNT(*) FROM dbo.GoodsReceiptLines l WHERE l.GoodsReceiptId=g.GoodsReceiptId),
           COALESCE((SELECT SUM(m.ValueChange) FROM dbo.InventoryMovements m WHERE m.DocumentId=g.GoodsReceiptId),0),
           CONCAT(g.DocumentNumber,N' ',g.SupplierInvoiceNumber,N' ',w.Name,N' ',s.DisplayName,N' ',
             (SELECT STRING_AGG(CONCAT(p.ProductCode,N' ',p.Reference,N' ',p.Name),N' ')
              FROM dbo.GoodsReceiptLines l INNER JOIN dbo.Products p ON p.ProductId=l.ProductId
              WHERE l.GoodsReceiptId=g.GoodsReceiptId))
    FROM dbo.GoodsReceipts g
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=g.WarehouseId AND w.UseForSales=1
    INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=g.SupplierId
    INNER JOIN dbo.Parties s ON s.PartyId=supplier.PartyId
    WHERE g.BusinessId=@BusinessId;

    SELECT COUNT(*) FROM #InventoryHistory
    WHERE BusinessId=@BusinessId AND (@WarehouseId IS NULL OR WarehouseId=@WarehouseId)
      AND (@DocumentType IS NULL OR DocumentType=@DocumentType) AND (@Status IS NULL OR Status=@Status)
      AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To)
      AND (@Search IS NULL OR SearchText LIKE @Pattern);

    SELECT DocumentId,DocumentType,DocumentNumber,WarehouseId,WarehouseName,
           DestinationWarehouseId,DestinationWarehouseName,ReasonCode,Status,OccurredAt,
           LineCount,CASE WHEN @IncludeCosts=1 THEN TotalValueChange END
    FROM #InventoryHistory
    WHERE BusinessId=@BusinessId AND (@WarehouseId IS NULL OR WarehouseId=@WarehouseId)
      AND (@DocumentType IS NULL OR DocumentType=@DocumentType) AND (@Status IS NULL OR Status=@Status)
      AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To)
      AND (@Search IS NULL OR SearchText LIKE @Pattern)
    ORDER BY OccurredAt DESC,DocumentId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
