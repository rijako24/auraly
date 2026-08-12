SET NOCOUNT ON;

INSERT dbo.ProductUnits
    (ProductUnitId,BusinessId,Code,Name,Symbol,AllowsFractionalQuantity,DecimalPlaces,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,v.Code,v.Name,v.Symbol,v.AllowsFractionalQuantity,v.DecimalPlaces,1,SYSUTCDATETIME()
FROM dbo.Businesses b
CROSS APPLY (VALUES
    (N'EA',N'Unidad',N'und',CAST(0 AS bit),CAST(0 AS tinyint)),
    (N'KG',N'Kilogramo',N'kg',CAST(1 AS bit),CAST(3 AS tinyint)),
    (N'M',N'Metro',N'm',CAST(1 AS bit),CAST(3 AS tinyint)),
    (N'L',N'Litro',N'L',CAST(1 AS bit),CAST(3 AS tinyint))
) v(Code,Name,Symbol,AllowsFractionalQuantity,DecimalPlaces)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ProductUnits currentUnit
    WHERE currentUnit.BusinessId=b.BusinessId AND currentUnit.Code=v.Code);
