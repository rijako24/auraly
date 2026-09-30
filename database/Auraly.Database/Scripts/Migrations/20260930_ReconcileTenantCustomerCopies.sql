SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
        THROW 52050, 'Customers table is missing.', 1;
    IF COL_LENGTH(N'dbo.Customers', N'TenantId') IS NULL
        THROW 52051, 'Customers must have TenantId before duplicate reconciliation.', 1;

    -- A new branch could copy the same customer role for a shared Party. Keep
    -- the oldest role and remove only equivalent copies with no FK references.
    DECLARE @Sql NVARCHAR(MAX);
    SELECT @Sql=STRING_AGG(CAST(
        N' AND NOT EXISTS(SELECT 1 FROM '
        + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.'
        + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' dependent WHERE dependent.'
        + QUOTENAME(COL_NAME(keyColumn.parent_object_id,keyColumn.parent_column_id))
        + N'=customer.CustomerId)' AS NVARCHAR(MAX)),N'')
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns keyColumn
      ON keyColumn.constraint_object_id=fk.object_id
    WHERE fk.referenced_object_id=OBJECT_ID(N'dbo.Customers')
      AND COL_NAME(keyColumn.referenced_object_id,keyColumn.referenced_column_id)=N'CustomerId';

    SET @Sql=N'
    ;WITH copies AS (
        SELECT CustomerId,TenantId,PartyId,
               ROW_NUMBER() OVER(
                 PARTITION BY TenantId,PartyId ORDER BY CreatedAt,CustomerId) copyNumber,
               FIRST_VALUE(CustomerId) OVER(
                 PARTITION BY TenantId,PartyId ORDER BY CreatedAt,CustomerId) canonicalId
        FROM dbo.Customers)
    DELETE customer
    FROM dbo.Customers customer
    JOIN copies ON copies.CustomerId=customer.CustomerId
    JOIN dbo.Customers canonical ON canonical.CustomerId=copies.canonicalId
    WHERE copies.copyNumber>1
      AND customer.RequiresElectronicInvoice=canonical.RequiresElectronicInvoice
      AND customer.IsActive=canonical.IsActive'
      + COALESCE(@Sql,N'') + N';
    IF EXISTS (
        SELECT 1 FROM dbo.Customers
        GROUP BY TenantId,PartyId HAVING COUNT(*)>1)
        THROW 52052, ''Conflicting tenant customers require manual reconciliation.'', 1;';
    EXEC sys.sp_executesql @Sql;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
