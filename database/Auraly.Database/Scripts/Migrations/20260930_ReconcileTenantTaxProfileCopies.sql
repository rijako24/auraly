SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    IF OBJECT_ID(N'dbo.TaxProfiles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Products', N'U') IS NULL
       OR COL_LENGTH(N'dbo.TaxProfiles', N'TenantId') IS NULL
        THROW 52053, 'Tenant tax profile reconciliation requires TaxProfiles and Products.', 1;

    -- The business cutover temporarily removes the composite Products tax FKs,
    -- so guard both product columns explicitly as well as every existing FK.
    DECLARE @Sql NVARCHAR(MAX);
    SELECT @Sql=STRING_AGG(CAST(
        N' AND NOT EXISTS(SELECT 1 FROM '
        + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.'
        + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' dependent WHERE dependent.'
        + QUOTENAME(COL_NAME(keyColumn.parent_object_id,keyColumn.parent_column_id))
        + N'=profile.TaxProfileId)' AS NVARCHAR(MAX)),N'')
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns keyColumn
      ON keyColumn.constraint_object_id=fk.object_id
    WHERE fk.referenced_object_id=OBJECT_ID(N'dbo.TaxProfiles')
      AND COL_NAME(keyColumn.referenced_object_id,keyColumn.referenced_column_id)=N'TaxProfileId';

    SET @Sql=N'
    ;WITH copies AS (
        SELECT TaxProfileId,TenantId,Code,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,Code ORDER BY CreatedAt,TaxProfileId) copyNumber,
               FIRST_VALUE(TaxProfileId) OVER(
                 PARTITION BY TenantId,Code ORDER BY CreatedAt,TaxProfileId) canonicalId
        FROM dbo.TaxProfiles)
    DELETE profile
    FROM dbo.TaxProfiles profile
    JOIN copies ON copies.TaxProfileId=profile.TaxProfileId
    JOIN dbo.TaxProfiles canonical ON canonical.TaxProfileId=copies.canonicalId
    WHERE copies.copyNumber>1
      AND NOT EXISTS (
        SELECT profile.DianTaxCode,profile.Name,profile.Rate,profile.IsActive
        EXCEPT
        SELECT canonical.DianTaxCode,canonical.Name,canonical.Rate,canonical.IsActive)
      AND NOT EXISTS(SELECT 1 FROM dbo.Products product WHERE product.TaxProfileId=profile.TaxProfileId)
      AND NOT EXISTS(SELECT 1 FROM dbo.Products product WHERE product.PurchaseTaxProfileId=profile.TaxProfileId)'
      + COALESCE(@Sql,N'') + N';
    IF EXISTS (
        SELECT 1 FROM dbo.TaxProfiles
        GROUP BY TenantId,Code HAVING COUNT(*)>1)
        THROW 52054, ''Conflicting tenant tax profiles require manual reconciliation.'', 1;';
    EXEC sys.sp_executesql @Sql;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
