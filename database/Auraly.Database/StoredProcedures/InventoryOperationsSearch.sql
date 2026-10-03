CREATE PROCEDURE dbo.InventoryOperationsSearch
    @BusinessId uniqueidentifier,
    @WarehouseId uniqueidentifier = NULL,
    @Search nvarchar(160) = NULL,
    @Pattern nvarchar(324) = NULL,
    @DocumentType nvarchar(80) = NULL,
    @Status nvarchar(80) = NULL,
    @From datetimeoffset = NULL,
    @To datetimeoffset = NULL,
    @ReasonCode nvarchar(80) = NULL,
    @DestinationWarehouseId uniqueidentifier = NULL,
    @SupplierId uniqueidentifier = NULL,
    @PurchaseEvidenceType nvarchar(40) = NULL,
    @IncludeCosts bit,
    @Offset int,
    @PageSize int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.InventoryOperationId DocumentId,o.DocumentType,o.DocumentNumber,o.BusinessId,
           o.WarehouseId,w.Name WarehouseName,o.DestinationWarehouseId,dw.Name DestinationWarehouseName,
           o.ReasonCode,o.Status,o.OccurredAt,
           (SELECT COUNT(*) FROM dbo.InventoryOperationLines l WHERE l.InventoryOperationId=o.InventoryOperationId) LineCount,
           o.TotalValueChange,
           o.ConversionInputEquivalent,o.ConversionOutputEquivalent,o.ConversionLossQuantity,
           o.ConversionLossPercent,o.ConversionMaximumLossPercent,
           CONCAT(o.DocumentNumber,N' ',o.ReasonDescription,N' ',o.ReasonCode,N' ',w.Name) SearchText,
           CAST(NULL AS uniqueidentifier) SupplierId,CAST(NULL AS nvarchar(40)) PurchaseEvidenceType
    INTO #InventoryHistory
    FROM dbo.InventoryOperations o
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.IsSystem=0
    LEFT JOIN dbo.Warehouses dw ON dw.WarehouseId=o.DestinationWarehouseId
    WHERE o.BusinessId=@BusinessId
    UNION ALL
    SELECT g.GoodsReceiptId,N'GoodsReceipt',g.DocumentNumber,g.BusinessId,g.WarehouseId,w.Name,NULL,NULL,
           N'GOODS_RECEIPT',g.Status,g.ReceivedAt,
           (SELECT COUNT(*) FROM dbo.GoodsReceiptLines l WHERE l.GoodsReceiptId=g.GoodsReceiptId),
           COALESCE((SELECT SUM(m.ValueChange) FROM dbo.InventoryMovements m WHERE m.DocumentId=g.GoodsReceiptId),0),
           NULL,NULL,NULL,NULL,NULL,
           CONCAT(g.DocumentNumber,N' ',g.SupplierInvoiceNumber,N' ',w.Name,N' ',s.DisplayName),g.SupplierId,g.PurchaseEvidenceType
    FROM dbo.GoodsReceipts g
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=g.WarehouseId AND w.IsSystem=0
    INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=g.SupplierId
    INNER JOIN dbo.Parties s ON s.PartyId=supplier.PartyId
    WHERE g.BusinessId=@BusinessId;

    CREATE TABLE #ProductSearchMatches(DocumentId uniqueidentifier NOT NULL PRIMARY KEY);
    IF @Search IS NOT NULL
    BEGIN
        INSERT #ProductSearchMatches(DocumentId)
        SELECT matched.DocumentId FROM (
            SELECT line.InventoryOperationId DocumentId
            FROM dbo.InventoryOperationLines line
            INNER JOIN dbo.InventoryOperations operation ON operation.InventoryOperationId=line.InventoryOperationId
              AND operation.BusinessId=@BusinessId
            INNER JOIN dbo.Products product ON product.ProductId=line.ProductId
            WHERE product.ProductCode LIKE @Pattern OR product.Reference LIKE @Pattern OR product.Name LIKE @Pattern
            UNION
            SELECT line.GoodsReceiptId
            FROM dbo.GoodsReceiptLines line
            INNER JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=line.GoodsReceiptId
              AND receipt.BusinessId=@BusinessId
            INNER JOIN dbo.Products product ON product.ProductId=line.ProductId
            WHERE product.ProductCode LIKE @Pattern OR product.Reference LIKE @Pattern OR product.Name LIKE @Pattern
        ) matched;
    END;

    SELECT COUNT(*) FROM #InventoryHistory history
    WHERE BusinessId=@BusinessId AND (@WarehouseId IS NULL OR WarehouseId=@WarehouseId)
      AND (@DocumentType IS NULL OR DocumentType=@DocumentType) AND (@Status IS NULL OR Status=@Status)
      AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To)
      AND (@ReasonCode IS NULL OR ReasonCode=@ReasonCode)
      AND (@DestinationWarehouseId IS NULL OR DestinationWarehouseId=@DestinationWarehouseId)
      AND (@SupplierId IS NULL OR SupplierId=@SupplierId)
      AND (@PurchaseEvidenceType IS NULL OR PurchaseEvidenceType=@PurchaseEvidenceType)
      AND (@Search IS NULL OR SearchText LIKE @Pattern OR EXISTS (
          SELECT 1 FROM #ProductSearchMatches matched WHERE matched.DocumentId=history.DocumentId));

    SELECT DocumentId,DocumentType,DocumentNumber,WarehouseId,WarehouseName,
           DestinationWarehouseId,DestinationWarehouseName,ReasonCode,Status,OccurredAt,
           LineCount,CASE WHEN @IncludeCosts=1 THEN TotalValueChange END,
           ConversionInputEquivalent,ConversionOutputEquivalent,ConversionLossQuantity,
           ConversionLossPercent,ConversionMaximumLossPercent
    FROM #InventoryHistory history
    WHERE BusinessId=@BusinessId AND (@WarehouseId IS NULL OR WarehouseId=@WarehouseId)
      AND (@DocumentType IS NULL OR DocumentType=@DocumentType) AND (@Status IS NULL OR Status=@Status)
      AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To)
      AND (@ReasonCode IS NULL OR ReasonCode=@ReasonCode)
      AND (@DestinationWarehouseId IS NULL OR DestinationWarehouseId=@DestinationWarehouseId)
      AND (@SupplierId IS NULL OR SupplierId=@SupplierId)
      AND (@PurchaseEvidenceType IS NULL OR PurchaseEvidenceType=@PurchaseEvidenceType)
      AND (@Search IS NULL OR SearchText LIKE @Pattern OR EXISTS (
          SELECT 1 FROM #ProductSearchMatches matched WHERE matched.DocumentId=history.DocumentId))
    ORDER BY OccurredAt DESC,DocumentId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
