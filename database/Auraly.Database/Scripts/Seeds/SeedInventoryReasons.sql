SET NOCOUNT ON;

DECLARE @Defaults TABLE(OperationType NVARCHAR(64),Code NVARCHAR(40),Name NVARCHAR(120),DisplayOrder INT);
INSERT @Defaults VALUES
(N'StockCount',N'PHYSICAL_COUNT',N'Conteo físico programado',10),
(N'StockCount',N'INVENTORY_VERIFICATION',N'Verificación de existencias',20),
(N'InventoryAdjustment',N'MANUAL_ADJUSTMENT',N'Corrección de saldo',10),
(N'InventoryAdjustment',N'INITIAL_BALANCE',N'Saldo inicial',20),
(N'InventoryAdjustment',N'FOUND_SURPLUS',N'Sobrante identificado',30),
(N'InventoryAdjustment',N'FOUND_SHORTAGE',N'Faltante identificado',40),
(N'WarehouseTransfer',N'WAREHOUSE_TRANSFER',N'Reabastecimiento entre bodegas',10),
(N'WarehouseTransfer',N'STOCK_REDISTRIBUTION',N'Redistribución de existencias',20),
(N'ProductConversion',N'PRESENTATION_CHANGE',N'Cambio de presentación',10),
(N'Damage',N'DAMAGE',N'Producto averiado',10),
(N'Damage',N'EXPIRED',N'Producto vencido',20),
(N'Damage',N'NOT_SALEABLE',N'Producto no vendible',30);

INSERT dbo.InventoryReasons(InventoryReasonId,BusinessId,OperationType,Code,Name,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
SELECT NEWID(),b.BusinessId,d.OperationType,d.Code,d.Name,1,1,d.DisplayOrder,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM dbo.Businesses b CROSS JOIN @Defaults d
WHERE b.IsActive=1 AND NOT EXISTS(
    SELECT 1 FROM dbo.InventoryReasons r
    WHERE r.BusinessId=b.BusinessId AND r.OperationType=d.OperationType AND r.Code=d.Code);

UPDATE operation
SET ReasonDescription=COALESCE(reason.Name,operation.ReasonCode)
FROM dbo.InventoryOperations operation
LEFT JOIN dbo.InventoryReasons reason ON reason.BusinessId=operation.BusinessId AND reason.OperationType=operation.DocumentType AND reason.Code=operation.ReasonCode
WHERE operation.ReasonDescription=N'';
