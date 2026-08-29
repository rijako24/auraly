IF OBJECT_ID(N'dbo.Products',N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Products',N'TenantId') IS NULL
        ALTER TABLE dbo.Products ADD TenantId UNIQUEIDENTIFIER NULL;

    UPDATE product SET TenantId=business.TenantId
    FROM dbo.Products product
    INNER JOIN dbo.Businesses business ON business.BusinessId=product.BusinessId
    WHERE product.TenantId IS NULL;

    IF EXISTS(SELECT 1 FROM dbo.Products WHERE TenantId IS NULL)
        THROW 51089,'No fue posible asignar tenant a todos los productos existentes.',1;

    IF EXISTS(
        SELECT 1 FROM dbo.Products
        WHERE ProductCode IS NOT NULL
        GROUP BY TenantId,ProductCode HAVING COUNT(*)>1)
        THROW 51090,'Existen codigos de producto duplicados entre sedes del mismo tenant. Deben consolidarse antes del despliegue.',1;
END;
