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
    @PageSize int,
    @AllowedDocumentTypesJson nvarchar(max) = N'[]'

AS
BEGIN
    SET NOCOUNT ON;
    -- Permissions and all header filters precede counting/paging. Line details
    -- and value aggregates are loaded only for the materialized page.
    WITH Headers AS (
        SELECT o.InventoryOperationId DocumentId,o.DocumentType,o.DocumentNumber,o.BusinessId,
            o.WarehouseId,w.Name WarehouseName,o.DestinationWarehouseId,dw.Name DestinationWarehouseName,
            o.ReasonCode,o.Status,o.OccurredAt,o.TotalValueChange,
            o.ConversionInputEquivalent,o.ConversionOutputEquivalent,o.ConversionLossQuantity,
            o.ConversionLossPercent,o.ConversionMaximumLossPercent,
            CONCAT(o.DocumentNumber,N' ',o.ReasonDescription,N' ',o.ReasonCode,N' ',w.Name) SearchText,
            CAST(NULL AS uniqueidentifier) SupplierId,CAST(NULL AS nvarchar(40)) PurchaseEvidenceType
        FROM dbo.InventoryOperations o
        JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.IsSystem=0
        LEFT JOIN dbo.Warehouses dw ON dw.WarehouseId=o.DestinationWarehouseId
        WHERE o.BusinessId=@BusinessId
          AND o.DocumentType IN (SELECT [value] FROM OPENJSON(@AllowedDocumentTypesJson))
        UNION ALL
        SELECT g.GoodsReceiptId,N'GoodsReceipt',g.DocumentNumber,g.BusinessId,g.WarehouseId,w.Name,NULL,NULL,
            N'GOODS_RECEIPT',g.Status,g.ReceivedAt,CAST(NULL AS decimal(19,6)),NULL,NULL,NULL,NULL,NULL,
            CONCAT(g.DocumentNumber,N' ',g.SupplierInvoiceNumber,N' ',w.Name,N' ',s.DisplayName),
            g.SupplierId,g.PurchaseEvidenceType
        FROM dbo.GoodsReceipts g
        JOIN dbo.Warehouses w ON w.WarehouseId=g.WarehouseId AND w.IsSystem=0
        JOIN dbo.Suppliers supplier ON supplier.SupplierId=g.SupplierId
        JOIN dbo.Parties s ON s.PartyId=supplier.PartyId
        WHERE g.BusinessId=@BusinessId
          AND N'GoodsReceipt' IN (SELECT [value] FROM OPENJSON(@AllowedDocumentTypesJson))
    )
    SELECT * INTO #Filtered FROM Headers history
    WHERE (@WarehouseId IS NULL OR WarehouseId=@WarehouseId)
        AND (@DocumentType IS NULL OR DocumentType=@DocumentType)
        AND (@Status IS NULL OR Status=@Status)
        AND (@From IS NULL OR OccurredAt>=@From) AND (@To IS NULL OR OccurredAt<@To)
        AND (@ReasonCode IS NULL OR ReasonCode=@ReasonCode)
        AND (@DestinationWarehouseId IS NULL OR DestinationWarehouseId=@DestinationWarehouseId)
        AND (@SupplierId IS NULL OR SupplierId=@SupplierId)
        AND (@PurchaseEvidenceType IS NULL OR PurchaseEvidenceType=@PurchaseEvidenceType)
        AND (@Search IS NULL OR SearchText LIKE @Pattern OR EXISTS(
            SELECT 1 FROM dbo.InventoryOperationLines line
            JOIN dbo.Products product ON product.ProductId=line.ProductId
            WHERE line.InventoryOperationId=history.DocumentId
              AND (product.ProductCode LIKE @Pattern OR product.Reference LIKE @Pattern OR product.Name LIKE @Pattern))
          OR EXISTS(SELECT 1 FROM dbo.GoodsReceiptLines line
            JOIN dbo.Products product ON product.ProductId=line.ProductId
            WHERE line.GoodsReceiptId=history.DocumentId
              AND (product.ProductCode LIKE @Pattern OR product.Reference LIKE @Pattern OR product.Name LIKE @Pattern)));

    SELECT COUNT(*) FROM #Filtered;
    SELECT * INTO #Page FROM #Filtered
    ORDER BY OccurredAt DESC,DocumentId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT DocumentId,DocumentType,DocumentNumber,WarehouseId,WarehouseName,
        DestinationWarehouseId,DestinationWarehouseName,ReasonCode,Status,OccurredAt,
        CASE WHEN DocumentType=N'GoodsReceipt'
            THEN (SELECT COUNT(*) FROM dbo.GoodsReceiptLines line WHERE line.GoodsReceiptId=history.DocumentId)
            ELSE (SELECT COUNT(*) FROM dbo.InventoryOperationLines line WHERE line.InventoryOperationId=history.DocumentId) END,
        CASE WHEN @IncludeCosts=1 THEN CASE WHEN DocumentType=N'GoodsReceipt'
            THEN COALESCE((SELECT SUM(m.ValueChange) FROM dbo.InventoryMovements m WHERE m.DocumentId=history.DocumentId),0)
            ELSE TotalValueChange END END,
        ConversionInputEquivalent,ConversionOutputEquivalent,ConversionLossQuantity,
        ConversionLossPercent,ConversionMaximumLossPercent
    FROM #Page history ORDER BY OccurredAt DESC,DocumentId;
END;
