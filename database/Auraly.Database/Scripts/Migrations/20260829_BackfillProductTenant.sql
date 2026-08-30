IF OBJECT_ID(N'dbo.Products',N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Products',N'TenantId') IS NULL
        ALTER TABLE dbo.Products ADD TenantId UNIQUEIDENTIFIER NULL;

    -- La columna puede no existir al compilar este mismo batch. El SQL dinamico
    -- difiere la resolucion del nombre hasta despues del ALTER y permite que el
    -- DACPAC haga posteriormente el cambio canonico a NOT NULL.
    EXEC sys.sp_executesql N'
        UPDATE product SET TenantId=business.TenantId
        FROM dbo.Products product
        INNER JOIN dbo.Businesses business ON business.BusinessId=product.BusinessId
        WHERE product.TenantId IS NULL;

        IF EXISTS(SELECT 1 FROM dbo.Products WHERE TenantId IS NULL)
            THROW 51089,''No fue posible asignar tenant a todos los productos existentes.'',1;

        IF EXISTS(
            SELECT 1 FROM dbo.Products
            WHERE ProductCode IS NOT NULL
            GROUP BY TenantId,ProductCode HAVING COUNT(*)>1)
            THROW 51090,''Existen codigos de producto duplicados entre sedes del mismo tenant. Deben consolidarse antes del despliegue.'',1;';

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Products')
          AND name = N'TenantId'
          AND is_nullable = 1)
        ALTER TABLE dbo.Products ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
END;
