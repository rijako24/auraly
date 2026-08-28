SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.Products', N'UnitPrice') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.ProductPrices', N'U') IS NULL
        THROW 51000, 'No se puede retirar Products.UnitPrice sin la tabla canonica ProductPrices.', 1;

    BEGIN TRANSACTION;

    -- SQL Server compila el lote antes de evaluar COL_LENGTH. El SQL dinamico
    -- mantiene esta migracion idempotente cuando la columna ya fue retirada.
    EXEC sys.sp_executesql N'
        INSERT dbo.ProductPrices
          (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
        SELECT NEWID(),p.BusinessId,p.ProductId,p.UnitPrice,p.UnitPrice,
               LEFT(UPPER(COALESCE(NULLIF(LTRIM(RTRIM(p.Currency)),N''''),N''COP'')),3),
               SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
        FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
        WHERE p.IsActive=1
          AND p.UnitPrice > 0
          AND NOT EXISTS (
            SELECT 1
            FROM dbo.ProductPrices pp WITH (UPDLOCK,HOLDLOCK)
            WHERE pp.BusinessId=p.BusinessId
              AND pp.ProductId=p.ProductId
              AND pp.IsActive=1);';

    DECLARE @defaultConstraint sysname = (
        SELECT dc.name
        FROM sys.default_constraints dc
        INNER JOIN sys.columns columnDefinition
            ON columnDefinition.object_id = dc.parent_object_id
           AND columnDefinition.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Products')
          AND columnDefinition.name = N'UnitPrice');

    IF @defaultConstraint IS NOT NULL
    BEGIN
        DECLARE @dropDefaultSql nvarchar(max) =
            N'ALTER TABLE dbo.Products DROP CONSTRAINT ' + QUOTENAME(@defaultConstraint) + N';';
        EXEC sys.sp_executesql @dropDefaultSql;
    END;

    ALTER TABLE dbo.Products DROP COLUMN UnitPrice;

    COMMIT TRANSACTION;
END;

PRINT 'RemoveProductsUnitPrice: ProductPrices conserva el precio publicado y Products.UnitPrice fue retirado.';
