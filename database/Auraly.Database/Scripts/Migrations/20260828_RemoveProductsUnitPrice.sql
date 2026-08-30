SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.Products', N'UnitPrice') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.ProductPrices', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProductPrices (
            ProductPriceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProductPrices PRIMARY KEY,
            BusinessId UNIQUEIDENTIFIER NOT NULL,
            ProductId UNIQUEIDENTIFIER NOT NULL,
            Amount DECIMAL(19,4) NOT NULL,
            PreparedAmount DECIMAL(19,4) NOT NULL CONSTRAINT DF_ProductPrices_PreparedAmount DEFAULT (0),
            CurrencyCode CHAR(3) NOT NULL,
            CostBasisType NVARCHAR(32) NULL,
            CostBasisAmount DECIMAL(19,6) NULL,
            TargetMarginPercent DECIMAL(9,6) NULL,
            EffectiveMarginPercent DECIMAL(9,6) NULL,
            InputMode NVARCHAR(16) NULL,
            RoundingIncrement DECIMAL(19,4) NULL,
            RoundingMode NVARCHAR(16) NULL,
            PublishedByUserId UNIQUEIDENTIFIER NULL,
            PublishedAt DATETIMEOFFSET(7) NULL,
            ValidFrom DATETIMEOFFSET(7) NOT NULL,
            ValidUntil DATETIMEOFFSET(7) NULL,
            IsActive BIT NOT NULL,
            CreatedAt DATETIMEOFFSET(7) NOT NULL,
            RowVersion ROWVERSION NOT NULL,
            CONSTRAINT FK_ProductPrices_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId),
            CONSTRAINT CK_ProductPrices_Amount CHECK (Amount >= 0),
            CONSTRAINT CK_ProductPrices_PreparedAmount CHECK (PreparedAmount >= 0),
            CONSTRAINT CK_ProductPrices_CostBasis CHECK (CostBasisAmount IS NULL OR CostBasisAmount >= 0),
            CONSTRAINT CK_ProductPrices_Margin CHECK (TargetMarginPercent IS NULL OR TargetMarginPercent BETWEEN 0 AND 99.999999),
            CONSTRAINT CK_ProductPrices_InputMode CHECK (InputMode IS NULL OR InputMode IN (N'Margin',N'SalePrice')),
            CONSTRAINT CK_ProductPrices_Rounding CHECK ((RoundingIncrement IS NULL AND RoundingMode IS NULL) OR (RoundingIncrement > 0 AND RoundingMode IN (N'Nearest',N'Up',N'Down'))),
            CONSTRAINT CK_ProductPrices_Validity CHECK (ValidUntil IS NULL OR ValidUntil > ValidFrom));

        CREATE UNIQUE INDEX UX_ProductPrices_Active
            ON dbo.ProductPrices(BusinessId,ProductId) WHERE IsActive=1;
    END;

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
