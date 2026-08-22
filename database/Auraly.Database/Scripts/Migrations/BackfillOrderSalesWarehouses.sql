;WITH Missing AS (
    SELECT o.OrderId,w.WarehouseId
    FROM dbo.Orders o
    CROSS APPLY (
        SELECT TOP(1) candidate.WarehouseId
        FROM dbo.Warehouses candidate
        WHERE candidate.BusinessId=o.BusinessId
          AND candidate.IsActive=1
          AND candidate.UseForSales=1
        ORDER BY CASE WHEN candidate.Code=N'VEN' THEN 0 ELSE 1 END,
                 candidate.CreatedAt,candidate.WarehouseId
    ) w
    WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId')) IS NULL
)
UPDATE target
SET CustomAttributesJson=JSON_MODIFY(
        CASE WHEN ISJSON(target.CustomAttributesJson)=1 THEN target.CustomAttributesJson ELSE N'{}' END,
        '$.WarehouseId',CONVERT(nvarchar(36),missing.WarehouseId)),
    UpdatedAt=SYSUTCDATETIME()
FROM dbo.Orders target
INNER JOIN Missing missing ON missing.OrderId=target.OrderId;

;WITH Missing AS (
    SELECT d.OrderDraftId,w.WarehouseId
    FROM dbo.OrderDrafts d
    CROSS APPLY (
        SELECT TOP(1) candidate.WarehouseId
        FROM dbo.Warehouses candidate
        WHERE candidate.BusinessId=d.BusinessId
          AND candidate.IsActive=1
          AND candidate.UseForSales=1
        ORDER BY CASE WHEN candidate.Code=N'VEN' THEN 0 ELSE 1 END,
                 candidate.CreatedAt,candidate.WarehouseId
    ) w
    WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(d.CustomAttributesJson,'$.WarehouseId')) IS NULL
)
UPDATE target
SET CustomAttributesJson=JSON_MODIFY(
        CASE WHEN ISJSON(target.CustomAttributesJson)=1 THEN target.CustomAttributesJson ELSE N'{}' END,
        '$.WarehouseId',CONVERT(nvarchar(36),missing.WarehouseId)),
    UpdatedAt=SYSUTCDATETIME()
FROM dbo.OrderDrafts target
INNER JOIN Missing missing ON missing.OrderDraftId=target.OrderDraftId;
