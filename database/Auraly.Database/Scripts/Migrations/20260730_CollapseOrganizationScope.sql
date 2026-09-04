/*
  Business is the canonical branch/sede. This migration removes the former
  BusinessLocations level only after proving that every persisted relationship
  resolves to the same Business.
*/
IF OBJECT_ID(N'dbo.BusinessLocations', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.BusinessLocations', N'BusinessId') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'IF EXISTS (
        SELECT 1
        FROM dbo.Warehouses w
        JOIN dbo.BusinessLocations l ON l.LocationId=w.LocationId
        WHERE l.BusinessId<>w.BusinessId)
        THROW 51120, ''A warehouse points to a location from another Business.'', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.CashRegisters r
        JOIN dbo.BusinessLocations l ON l.LocationId=r.LocationId
        JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
        WHERE l.BusinessId<>r.BusinessId
           OR w.BusinessId<>r.BusinessId
           OR w.LocationId<>r.LocationId)
        THROW 51121, ''A register has an inconsistent Business, warehouse or location.'', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.PosDevices d
        JOIN dbo.BusinessLocations l ON l.LocationId=d.LocationId
        JOIN dbo.CashRegisters r ON r.RegisterId=d.RegisterId
        WHERE l.BusinessId<>d.BusinessId
           OR r.BusinessId<>d.BusinessId
           OR r.WarehouseId<>d.WarehouseId
           OR r.LocationId<>d.LocationId)
        THROW 51122, ''A POS device has an inconsistent Business, warehouse, register or location.'', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.PosEnrollmentSessions e
        JOIN dbo.BusinessLocations l ON l.LocationId=e.LocationId
        JOIN dbo.CashRegisters r ON r.RegisterId=e.RegisterId
        WHERE l.BusinessId<>e.BusinessId
           OR r.BusinessId<>e.BusinessId
           OR r.WarehouseId<>e.WarehouseId
           OR r.LocationId<>e.LocationId)
        THROW 51123, ''A POS enrollment has an inconsistent organizational scope.'', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.DocumentSeries s
        JOIN dbo.BusinessLocations l ON l.LocationId=s.LocationId
        WHERE l.BusinessId<>s.BusinessId)
        THROW 51124, ''A document series points to a location from another Business.'', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.CashSessions s
        JOIN dbo.BusinessLocations l ON l.LocationId=s.LocationId
        JOIN dbo.CashRegisters r ON r.RegisterId=s.RegisterId
        WHERE l.BusinessId<>s.BusinessId OR r.BusinessId<>s.BusinessId)
        THROW 51125, ''A cash session has an inconsistent Business or register.'', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.SalesDocuments d
        JOIN dbo.BusinessLocations l ON l.LocationId=d.LocationId
        JOIN dbo.CashRegisters r ON r.RegisterId=d.RegisterId
        WHERE l.BusinessId<>d.BusinessId
           OR r.BusinessId<>d.BusinessId
           OR r.WarehouseId<>d.WarehouseId)
        THROW 51126, ''A sales document has an inconsistent organizational scope.'', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.SalesDrafts d
        JOIN dbo.BusinessLocations l ON l.LocationId=d.LocationId
        JOIN dbo.CashRegisters r ON r.RegisterId=d.RegisterId
        WHERE l.BusinessId<>d.BusinessId
           OR r.BusinessId<>d.BusinessId
           OR r.WarehouseId<>d.WarehouseId)
        THROW 51127, ''A sales draft has an inconsistent organizational scope.'', 1;

    DECLARE @DropForeignKeys nvarchar(max)=N'''';
    SELECT @DropForeignKeys +=
        N''ALTER TABLE ''+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+
        N''.''+QUOTENAME(OBJECT_NAME(parent_object_id))+
        N'' DROP CONSTRAINT ''+QUOTENAME(name)+N'';''
    FROM sys.foreign_keys
    WHERE referenced_object_id=OBJECT_ID(N''dbo.BusinessLocations'');
    EXEC sys.sp_executesql @DropForeignKeys;

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N''dbo.DocumentSeries'')
          AND name=N''CK_DocumentSeries_Scope'')
        ALTER TABLE dbo.DocumentSeries DROP CONSTRAINT CK_DocumentSeries_Scope;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N''dbo.Warehouses'')
          AND name=N''IX_Warehouses_Business_Location'')
        DROP INDEX IX_Warehouses_Business_Location ON dbo.Warehouses;

    DECLARE @DropColumns nvarchar(max)=N'''';
    SELECT @DropColumns +=
        N''ALTER TABLE dbo.''+QUOTENAME(v.TableName)+N'' DROP COLUMN LocationId;''
    FROM (VALUES
        (N''Warehouses''),
        (N''CashRegisters''),
        (N''PosDevices''),
        (N''PosEnrollmentSessions''),
        (N''DocumentSeries''),
        (N''CashSessions''),
        (N''SalesDocuments''),
        (N''SalesDrafts'')) v(TableName)
    WHERE COL_LENGTH(N''dbo.''+v.TableName, N''LocationId'') IS NOT NULL;
    EXEC sys.sp_executesql @DropColumns;

    DROP TABLE dbo.BusinessLocations;';
END;

-- A deployment interrupted after removing the legacy columns can leave the
-- unmodeled table behind because releases intentionally preserve objects that
-- are not in the DACPAC. Finish that cleanup without re-reading removed fields.
IF OBJECT_ID(N'dbo.BusinessLocations', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.BusinessLocations', N'BusinessId') IS NULL
BEGIN
    DECLARE @DropRemainingLocationForeignKeys nvarchar(max)=N'';
    SELECT @DropRemainingLocationForeignKeys +=
        N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+
        N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))+
        N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys
    WHERE referenced_object_id=OBJECT_ID(N'dbo.BusinessLocations');
    EXEC sys.sp_executesql @DropRemainingLocationForeignKeys;

    DROP TABLE dbo.BusinessLocations;
END;
