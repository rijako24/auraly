SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.InventoryOperations', N'U') IS NOT NULL
BEGIN
    INSERT dbo.Warehouses
    (
        WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
        PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,
        IsInventoryVisible,IsActive,CreatedAt
    )
    SELECT NEWID(),business.BusinessId,N'AVE',N'Bodega de averías',0,
           N'LatestReceiptCost',1,0,0,0,1,SYSUTCDATETIME()
    FROM dbo.Businesses business
    WHERE EXISTS
    (
        SELECT 1
        FROM dbo.InventoryOperations operation
        WHERE operation.BusinessId=business.BusinessId
          AND operation.DocumentType=N'Damage'
          AND operation.DestinationWarehouseId IS NULL
    )
      AND NOT EXISTS
    (
        SELECT 1 FROM dbo.Warehouses warehouse
        WHERE warehouse.BusinessId=business.BusinessId
          AND warehouse.Code=N'AVE'
    );

    UPDATE operation
    SET DestinationWarehouseId=warehouse.WarehouseId
    FROM dbo.InventoryOperations operation
    INNER JOIN dbo.Warehouses warehouse
        ON warehouse.BusinessId=operation.BusinessId
       AND warehouse.Code=N'AVE'
    WHERE operation.DocumentType=N'Damage'
      AND operation.DestinationWarehouseId IS NULL;
END;

COMMIT TRANSACTION;
