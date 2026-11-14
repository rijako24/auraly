SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.InventoryOperations', N'U') IS NOT NULL
BEGIN
    -- The original schema did not have DamageDisposition. Current irreversible
    -- damage deliberately has no destination and must never enter this backfill.
    DECLARE @LegacyDamageOperationScope TABLE(InventoryOperationId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier);
    DECLARE @LegacyDamageScopeSql nvarchar(max)=N'
        SELECT operation.InventoryOperationId,operation.BusinessId
        FROM dbo.InventoryOperations operation WITH(UPDLOCK,HOLDLOCK)
        WHERE operation.DocumentType=N''Damage'' AND operation.DestinationWarehouseId IS NULL';
    IF COL_LENGTH(N'dbo.InventoryOperations',N'DamageDisposition') IS NOT NULL
        SET @LegacyDamageScopeSql+=N' AND operation.DamageDisposition IS NULL';
    INSERT @LegacyDamageOperationScope EXEC sys.sp_executesql @LegacyDamageScopeSql;

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
        SELECT 1 FROM @LegacyDamageOperationScope operation
        WHERE operation.BusinessId=business.BusinessId
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
    INNER JOIN @LegacyDamageOperationScope legacy ON legacy.InventoryOperationId=operation.InventoryOperationId
    INNER JOIN dbo.Warehouses warehouse
        ON warehouse.BusinessId=operation.BusinessId
       AND warehouse.Code=N'AVE'
    WHERE operation.DocumentType=N'Damage'
      AND operation.DestinationWarehouseId IS NULL;
END;

COMMIT TRANSACTION;
