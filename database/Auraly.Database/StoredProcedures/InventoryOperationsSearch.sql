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

    -- Aggregate details only for the materialized page, never once per row.
    SELECT line.InventoryOperationId DocumentId,COUNT(*) LineCount INTO #OperationCounts
    FROM dbo.InventoryOperationLines line JOIN #Page page ON page.DocumentId=line.InventoryOperationId
    WHERE page.DocumentType<>N'GoodsReceipt' GROUP BY line.InventoryOperationId;
    SELECT line.GoodsReceiptId DocumentId,COUNT(*) LineCount INTO #ReceiptCounts
    FROM dbo.GoodsReceiptLines line JOIN #Page page ON page.DocumentId=line.GoodsReceiptId
    WHERE page.DocumentType=N'GoodsReceipt' GROUP BY line.GoodsReceiptId;
    SELECT movement.DocumentId,SUM(movement.ValueChange) TotalValue INTO #ReceiptValues
    FROM dbo.InventoryMovements movement JOIN #Page page ON page.DocumentId=movement.DocumentId
    WHERE @IncludeCosts=1 AND page.DocumentType=N'GoodsReceipt' AND movement.BusinessId=@BusinessId
    GROUP BY movement.DocumentId;

    SELECT history.DocumentId,history.DocumentType,history.DocumentNumber,history.WarehouseId,history.WarehouseName,
        history.DestinationWarehouseId,history.DestinationWarehouseName,history.ReasonCode,history.Status,history.OccurredAt,
        CASE WHEN history.DocumentType=N'GoodsReceipt' THEN COALESCE(receipt.LineCount,0) ELSE COALESCE(operation.LineCount,0) END,
        CASE WHEN @IncludeCosts=1 THEN CASE WHEN history.DocumentType=N'GoodsReceipt'
            THEN COALESCE(valuation.TotalValue,0) ELSE history.TotalValueChange END END,
        history.ConversionInputEquivalent,history.ConversionOutputEquivalent,history.ConversionLossQuantity,
        history.ConversionLossPercent,history.ConversionMaximumLossPercent,document.Label,status.Label,
        CASE WHEN history.DocumentType=N'GoodsReceipt' THEN document.Label ELSE reason.Name END ReasonName
    FROM #Page history
    LEFT JOIN #OperationCounts operation ON operation.DocumentId=history.DocumentId
    LEFT JOIN #ReceiptCounts receipt ON receipt.DocumentId=history.DocumentId
    LEFT JOIN #ReceiptValues valuation ON valuation.DocumentId=history.DocumentId
    LEFT JOIN reference.Options document ON document.CatalogCode=N'accounting-document-type' AND document.Code=history.DocumentType
    LEFT JOIN reference.Options status ON status.CatalogCode=N'inventory-operation-status' AND status.Code=history.Status
    LEFT JOIN dbo.BusinessReasons reason ON reason.BusinessId=@BusinessId AND reason.ReasonType=history.DocumentType AND reason.Code=history.ReasonCode
    ORDER BY history.OccurredAt DESC,history.DocumentId;
END;
