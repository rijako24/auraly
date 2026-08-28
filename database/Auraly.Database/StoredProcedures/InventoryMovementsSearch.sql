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
    INNER JOIN dbo.Products p ON p.ProductId=m.ProductId AND p.BusinessId=m.BusinessId
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
                   WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));

    SELECT m.InventoryMovementId,m.WarehouseId,w.Name,m.ProductId,COALESCE(p.ProductCode,N''),p.Name,
           m.DocumentId,m.DocumentType,o.DocumentNumber,m.MovementType,m.QuantityChange,
           COALESCE(m.QuantityBefore,0),COALESCE(m.QuantityAfter,0),
           CASE WHEN @IncludeCosts=1 THEN m.RecognizedUnitCost END,
           CASE WHEN @IncludeCosts=1 THEN m.ValueChange END,m.OccurredAt,m.PostedAt
    FROM dbo.InventoryMovements m
    INNER JOIN dbo.Products p ON p.ProductId=m.ProductId AND p.BusinessId=m.BusinessId
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
                   WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
    ORDER BY m.ProcessingSequence DESC,m.LineNumber
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
