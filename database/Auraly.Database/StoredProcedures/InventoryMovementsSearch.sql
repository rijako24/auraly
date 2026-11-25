CREATE PROCEDURE dbo.InventoryMovementsSearch
    @BusinessId uniqueidentifier,
    @WarehouseId uniqueidentifier = NULL,
    @ProductId uniqueidentifier = NULL,
    @Search nvarchar(160) = NULL,
    @Pattern nvarchar(324) = NULL,
    @DocumentType nvarchar(80) = NULL,
    @MovementType nvarchar(80) = NULL,
    @From datetimeoffset = NULL,
    @To datetimeoffset = NULL,
    @IncludeCosts bit,
    @Offset int,
    @PageSize int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.InventoryMovements m
    INNER JOIN dbo.Products p ON p.ProductId=m.ProductId
      AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=m.BusinessId)
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=m.WarehouseId AND w.BusinessId=m.BusinessId
    LEFT JOIN dbo.InventoryOperations o ON o.InventoryOperationId=m.DocumentId
    WHERE m.BusinessId=@BusinessId AND w.IsSystem=0
      AND (@WarehouseId IS NULL OR m.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR m.ProductId=@ProductId)
      AND (@DocumentType IS NULL OR m.DocumentType=@DocumentType)
      AND (@MovementType IS NULL OR m.MovementType=@MovementType)
      AND (@From IS NULL OR m.OccurredAt>=@From) AND (@To IS NULL OR m.OccurredAt<@To)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR o.DocumentNumber LIKE @Pattern
        OR m.DocumentType LIKE @Pattern OR m.MovementType LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.TenantId=p.TenantId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));

    SELECT m.InventoryMovementId,m.WarehouseId,w.Name,m.ProductId,COALESCE(p.ProductCode,N'') ProductCode,p.Name ProductName,
           m.DocumentId,m.DocumentType,o.DocumentNumber,m.MovementType,m.QuantityChange,
           COALESCE(m.QuantityBefore,0) QuantityBefore,COALESCE(m.QuantityAfter,0) QuantityAfter,
           CASE WHEN @IncludeCosts=1 THEN m.RecognizedUnitCost END RecognizedUnitCost,
           CASE WHEN @IncludeCosts=1 THEN m.ValueChange END ValueChange,m.OccurredAt,m.PostedAt,m.ProcessingSequence,m.LineNumber
    INTO #Page
    FROM dbo.InventoryMovements m
    INNER JOIN dbo.Products p ON p.ProductId=m.ProductId
      AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=m.BusinessId)
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=m.WarehouseId AND w.BusinessId=m.BusinessId
    LEFT JOIN dbo.InventoryOperations o ON o.InventoryOperationId=m.DocumentId
    WHERE m.BusinessId=@BusinessId AND w.IsSystem=0
      AND (@WarehouseId IS NULL OR m.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR m.ProductId=@ProductId)
      AND (@DocumentType IS NULL OR m.DocumentType=@DocumentType)
      AND (@MovementType IS NULL OR m.MovementType=@MovementType)
      AND (@From IS NULL OR m.OccurredAt>=@From) AND (@To IS NULL OR m.OccurredAt<@To)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR o.DocumentNumber LIKE @Pattern
        OR m.DocumentType LIKE @Pattern OR m.MovementType LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.TenantId=p.TenantId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
    ORDER BY m.ProcessingSequence DESC,m.LineNumber,m.InventoryMovementId DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
    SELECT page.InventoryMovementId,page.WarehouseId,page.Name,page.ProductId,page.ProductCode,page.ProductName,
      page.DocumentId,page.DocumentType,page.DocumentNumber,page.MovementType,page.QuantityChange,
      page.QuantityBefore,page.QuantityAfter,page.RecognizedUnitCost,page.ValueChange,page.OccurredAt,page.PostedAt,
      document.Label DocumentTypeLabel,movement.Label MovementTypeLabel
    FROM #Page page
    LEFT JOIN reference.Options document ON document.CatalogCode=N'accounting-document-type' AND document.Code=page.DocumentType
    LEFT JOIN reference.Options movement ON movement.CatalogCode=N'inventory-movement-type' AND movement.Code=page.MovementType
    ORDER BY page.ProcessingSequence DESC,page.LineNumber,page.InventoryMovementId DESC;
END;
