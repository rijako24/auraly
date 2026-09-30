SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @Tables TABLE (TableName SYSNAME NOT NULL PRIMARY KEY);
    INSERT @Tables (TableName) VALUES
        (N'Products'),(N'ProductCategories'),(N'ProductBrands'),(N'ProductUnits'),
        (N'TaxProfiles'),(N'ProductBarcodes'),(N'ProductIdentifiers'),(N'ProductLinks'),
        (N'ProductAliases'),(N'ProductImages'),(N'ProductOffers'),
        (N'ProductSearchTerms'),(N'ProductRecommendationRules'),
        (N'Suppliers'),(N'SupplierProducts'),(N'PriceChannels'),
        (N'Customers'),(N'Employees'),(N'CommerceSellers'),(N'Carriers'),
        (N'CounterpartyTaxProfiles');

    IF COL_LENGTH(N'dbo.Products', N'BusinessId') IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM @Tables
                   WHERE COL_LENGTH(N'dbo.' + TableName, N'BusinessId') IS NOT NULL)
            THROW 52049, 'Commerce master migration is only partially applied.', 1;
        COMMIT TRANSACTION;
        RETURN;
    END;

    DECLARE @TableName SYSNAME;
    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @ObjectId INT;
    DECLARE tableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @Tables;
    OPEN tableCursor;
    FETCH NEXT FROM tableCursor INTO @TableName;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @ObjectId = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName));
        IF @ObjectId IS NULL
            THROW 52041, 'Commerce master table is missing.', 1;

        IF COL_LENGTH(N'dbo.' + @TableName, N'BusinessId') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.' + @TableName, N'TenantId') IS NULL
            BEGIN
                SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName)
                    + N' ADD TenantId UNIQUEIDENTIFIER NULL;';
                EXEC sys.sp_executesql @Sql;
            END;
            SET @Sql = N'UPDATE master SET TenantId=business.TenantId '
                + N'FROM dbo.' + QUOTENAME(@TableName) + N' master '
                + N'JOIN dbo.Businesses business ON business.BusinessId=master.BusinessId '
                + N'WHERE master.TenantId IS NULL; '
                + N'IF EXISTS(SELECT 1 FROM dbo.' + QUOTENAME(@TableName) + N' master '
                + N'LEFT JOIN dbo.Businesses business ON business.BusinessId=master.BusinessId '
                + N'WHERE business.BusinessId IS NULL OR master.TenantId<>business.TenantId) '
                + N'THROW 52042, ''Commerce master has a cross-tenant or missing business reference.'', 1;';
            EXEC sys.sp_executesql @Sql;
        END;
        FETCH NEXT FROM tableCursor INTO @TableName;
    END;
    CLOSE tableCursor;
    DEALLOCATE tableCursor;

    -- Business creation copied these rows in DEV. Identical copies carry no
    -- independent identity or references; conflicting assignments must fail.
    EXEC sys.sp_executesql N'
    IF EXISTS (
        SELECT 1 FROM dbo.ProductBarcodes firstRow
        JOIN dbo.ProductBarcodes secondRow
          ON secondRow.TenantId=firstRow.TenantId AND secondRow.Barcode=firstRow.Barcode
         AND secondRow.ProductBarcodeId<>firstRow.ProductBarcodeId
        WHERE secondRow.ProductId<>firstRow.ProductId
           OR secondRow.IsPrimary<>firstRow.IsPrimary
           OR secondRow.IsActive<>firstRow.IsActive)
        THROW 52043, ''Conflicting tenant barcode copies require manual reconciliation.'', 1;

    ;WITH copies AS (
        SELECT ProductBarcodeId,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,Barcode ORDER BY CreatedAt,ProductBarcodeId) copyNumber
        FROM dbo.ProductBarcodes)
    DELETE barcode
    FROM dbo.ProductBarcodes barcode
    JOIN copies ON copies.ProductBarcodeId=barcode.ProductBarcodeId
    WHERE copies.copyNumber>1;

    IF EXISTS (
        SELECT 1 FROM dbo.ProductIdentifiers firstRow
        JOIN dbo.ProductIdentifiers secondRow
          ON secondRow.TenantId=firstRow.TenantId
         AND secondRow.IdentifierType=firstRow.IdentifierType
         AND secondRow.Value=firstRow.Value
         AND secondRow.ProductIdentifierId<>firstRow.ProductIdentifierId
        WHERE secondRow.ProductId<>firstRow.ProductId
           OR secondRow.IsActive<>firstRow.IsActive)
        THROW 52044, ''Conflicting tenant product identifier copies require manual reconciliation.'', 1;

    ;WITH copies AS (
        SELECT ProductIdentifierId,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,IdentifierType,Value ORDER BY CreatedAt,ProductIdentifierId) copyNumber
        FROM dbo.ProductIdentifiers)
    DELETE identifier
    FROM dbo.ProductIdentifiers identifier
    JOIN copies ON copies.ProductIdentifierId=identifier.ProductIdentifierId
    WHERE copies.copyNumber>1;

    IF EXISTS (
        SELECT 1 FROM dbo.ProductUnits firstRow
        JOIN dbo.ProductUnits secondRow
          ON secondRow.TenantId=firstRow.TenantId AND secondRow.Code=firstRow.Code
         AND secondRow.ProductUnitId<>firstRow.ProductUnitId
        WHERE secondRow.Name<>firstRow.Name
           OR EXISTS (SELECT secondRow.Symbol EXCEPT SELECT firstRow.Symbol)
           OR secondRow.AllowsFractionalQuantity<>firstRow.AllowsFractionalQuantity
           OR secondRow.DecimalPlaces<>firstRow.DecimalPlaces
           OR secondRow.IsActive<>firstRow.IsActive)
        THROW 52045, ''Conflicting tenant unit copies require manual reconciliation.'', 1;

    ;WITH copies AS (
        SELECT ProductUnitId,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,Code ORDER BY CreatedAt,ProductUnitId) copyNumber
        FROM dbo.ProductUnits)
    DELETE unit
    FROM dbo.ProductUnits unit
    JOIN copies ON copies.ProductUnitId=unit.ProductUnitId
    WHERE copies.copyNumber>1;

    ';

    -- A newly created branch can contain an identical supplier copy. Remove
    -- only later copies with the same commercial settings and no FK references.
    -- Preserve their Party row: other roles can still reference that identity.
    -- Any remaining duplicate is a real conflict and keeps the migration atomic.
    SELECT @Sql=STRING_AGG(CAST(
        N' AND NOT EXISTS(SELECT 1 FROM '
        + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.'
        + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' dependent WHERE dependent.'
        + QUOTENAME(COL_NAME(keyColumn.parent_object_id,keyColumn.parent_column_id))
        + N'=supplier.SupplierId)' AS NVARCHAR(MAX)),N'')
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns keyColumn
      ON keyColumn.constraint_object_id=fk.object_id
    WHERE fk.referenced_object_id=OBJECT_ID(N'dbo.Suppliers')
      AND COL_NAME(keyColumn.referenced_object_id,keyColumn.referenced_column_id)=N'SupplierId';

    SET @Sql=N'
    ;WITH copies AS (
        SELECT SupplierId,TenantId,Identification,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,Identification ORDER BY CreatedAt,SupplierId) copyNumber,
               FIRST_VALUE(SupplierId) OVER(
                 PARTITION BY TenantId,Identification ORDER BY CreatedAt,SupplierId) canonicalId
        FROM dbo.Suppliers)
    DELETE supplier
    FROM dbo.Suppliers supplier
    JOIN copies ON copies.SupplierId=supplier.SupplierId
    JOIN dbo.Suppliers canonical ON canonical.SupplierId=copies.canonicalId
    WHERE copies.copyNumber>1
      AND NOT EXISTS (
        SELECT supplier.Name,supplier.PurchaseEvidencePolicy,
               supplier.DefaultPaymentDueDays,supplier.IsActive
        EXCEPT
        SELECT canonical.Name,canonical.PurchaseEvidencePolicy,
               canonical.DefaultPaymentDueDays,canonical.IsActive)'
      + COALESCE(@Sql,N'') + N';
    IF EXISTS (
        SELECT 1 FROM dbo.Suppliers
        GROUP BY TenantId,Identification HAVING COUNT(*)>1)
        THROW 52046, ''Conflicting tenant suppliers require manual reconciliation.'', 1;';
    EXEC sys.sp_executesql @Sql;

    IF COL_LENGTH(N'dbo.CounterpartyTaxProfiles', N'BusinessId') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            IF EXISTS (
                SELECT 1 FROM dbo.CounterpartyTaxProfiles firstRow
                JOIN dbo.CounterpartyTaxProfiles secondRow
                  ON secondRow.TenantId=firstRow.TenantId
                 AND secondRow.CounterpartyId=firstRow.CounterpartyId
                 AND secondRow.BusinessId<>firstRow.BusinessId
                WHERE EXISTS (
                  SELECT secondRow.AppliesWithholding,secondRow.Responsibilities,secondRow.JurisdictionCode
                  EXCEPT
                  SELECT firstRow.AppliesWithholding,firstRow.Responsibilities,firstRow.JurisdictionCode))
                THROW 52048, ''Conflicting tenant counterparty tax profiles require manual reconciliation.'', 1;

            ;WITH copies AS (
                SELECT BusinessId,CounterpartyId,
                       ROW_NUMBER() OVER(
                         PARTITION BY TenantId,CounterpartyId ORDER BY UpdatedAt DESC,BusinessId) copyNumber
                FROM dbo.CounterpartyTaxProfiles)
            DELETE profile
            FROM dbo.CounterpartyTaxProfiles profile
            JOIN copies ON copies.BusinessId=profile.BusinessId
              AND copies.CounterpartyId=profile.CounterpartyId
            WHERE copies.copyNumber>1;';
    END;

    DECLARE dependencyCursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @Tables;
    OPEN dependencyCursor;
    FETCH NEXT FROM dependencyCursor INTO @TableName;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @ObjectId = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName));
        IF COL_LENGTH(N'dbo.' + @TableName, N'BusinessId') IS NOT NULL
        BEGIN
            -- Drop only dependencies on the removed column. The DACPAC restores
            -- tenant FKs and tenant indexes from the reviewed target schema.
            SELECT @Sql=STRING_AGG(
                CAST(N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
                    + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
                    + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' AS NVARCHAR(MAX)),
                CHAR(10))
            FROM sys.foreign_keys fk
            WHERE EXISTS (
                SELECT 1
                FROM sys.foreign_key_columns keyColumn
                JOIN sys.columns columnValue
                  ON columnValue.object_id=keyColumn.parent_object_id
                 AND columnValue.column_id=keyColumn.parent_column_id
                WHERE keyColumn.constraint_object_id=fk.object_id
                  AND keyColumn.parent_object_id=@ObjectId
                  AND columnValue.name=N'BusinessId')
               OR EXISTS (
                SELECT 1
                FROM sys.foreign_key_columns keyColumn
                JOIN sys.columns columnValue
                  ON columnValue.object_id=keyColumn.referenced_object_id
                 AND columnValue.column_id=keyColumn.referenced_column_id
                WHERE keyColumn.constraint_object_id=fk.object_id
                  AND keyColumn.referenced_object_id=@ObjectId
                  AND columnValue.name=N'BusinessId');
            IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;

            SELECT @Sql=STRING_AGG(
                CAST(N'DROP INDEX ' + QUOTENAME(idx.name) + N' ON dbo.'
                    + QUOTENAME(@TableName) + N';' AS NVARCHAR(MAX)), CHAR(10))
            FROM sys.indexes idx
            WHERE idx.object_id=@ObjectId AND idx.is_primary_key=0
              AND idx.is_unique_constraint=0
              AND EXISTS (
                SELECT 1 FROM sys.index_columns keyColumn
                JOIN sys.columns columnValue
                  ON columnValue.object_id=keyColumn.object_id
                 AND columnValue.column_id=keyColumn.column_id
                WHERE keyColumn.object_id=idx.object_id
                  AND keyColumn.index_id=idx.index_id
                  AND columnValue.name=N'BusinessId');
            IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;

            SELECT @Sql=STRING_AGG(
                CAST(N'ALTER TABLE dbo.' + QUOTENAME(@TableName)
                    + N' DROP CONSTRAINT ' + QUOTENAME(keyValue.name) + N';'
                    AS NVARCHAR(MAX)), CHAR(10))
            FROM sys.key_constraints keyValue
            WHERE keyValue.parent_object_id=@ObjectId
              AND EXISTS (
                SELECT 1 FROM sys.index_columns keyColumn
                JOIN sys.columns columnValue
                  ON columnValue.object_id=keyColumn.object_id
                 AND columnValue.column_id=keyColumn.column_id
                WHERE keyColumn.object_id=keyValue.parent_object_id
                  AND keyColumn.index_id=keyValue.unique_index_id
                  AND columnValue.name=N'BusinessId');
            IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;

            SET @Sql=N'ALTER TABLE dbo.' + QUOTENAME(@TableName)
                + N' DROP COLUMN BusinessId;';
            EXEC sys.sp_executesql @Sql;
        END;
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id=@ObjectId AND name=N'TenantId' AND is_nullable=1)
        BEGIN
            SET @Sql=N'IF EXISTS(SELECT 1 FROM dbo.' + QUOTENAME(@TableName)
                + N' WHERE TenantId IS NULL) '
                + N'THROW 52047, ''Commerce master TenantId is null.'', 1; '
                + N'ALTER TABLE dbo.' + QUOTENAME(@TableName)
                + N' ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;';
            EXEC sys.sp_executesql @Sql;
        END;
        FETCH NEXT FROM dependencyCursor INTO @TableName;
    END;
    CLOSE dependencyCursor;
    DEALLOCATE dependencyCursor;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
