/*
  Business is the canonical branch/sede. BusinessLocations and LocationId are
  retired compatibility artifacts. BusinessId on each canonical table already
  owns the persisted scope, so this migration only removes whatever legacy
  relationships remain after either a complete or an interrupted deployment.
*/
IF OBJECT_ID(N'dbo.BusinessLocations', N'U') IS NOT NULL
BEGIN
    DECLARE @DropLegacyLocationForeignKeys nvarchar(max)=N'';
    SELECT @DropLegacyLocationForeignKeys +=
        N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+
        N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))+
        N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys
    WHERE referenced_object_id=OBJECT_ID(N'dbo.BusinessLocations');
    EXEC sys.sp_executesql @DropLegacyLocationForeignKeys;

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentSeries')
          AND name=N'CK_DocumentSeries_Scope')
        ALTER TABLE dbo.DocumentSeries DROP CONSTRAINT CK_DocumentSeries_Scope;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.Warehouses')
          AND name=N'IX_Warehouses_Business_Location')
        DROP INDEX IX_Warehouses_Business_Location ON dbo.Warehouses;

    DECLARE @DropLegacyLocationColumns nvarchar(max)=N'';
    SELECT @DropLegacyLocationColumns +=
        N'ALTER TABLE dbo.'+QUOTENAME(v.TableName)+N' DROP COLUMN LocationId;'
    FROM (VALUES
        (N'Warehouses'),
        (N'CashRegisters'),
        (N'PosDevices'),
        (N'PosEnrollmentSessions'),
        (N'DocumentSeries'),
        (N'CashSessions'),
        (N'SalesDocuments'),
        (N'SalesDrafts')) v(TableName)
    WHERE COL_LENGTH(N'dbo.'+v.TableName, N'LocationId') IS NOT NULL;
    EXEC sys.sp_executesql @DropLegacyLocationColumns;

    DROP TABLE dbo.BusinessLocations;
END;
