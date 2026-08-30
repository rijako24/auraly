IF COL_LENGTH(N'dbo.FiscalSeries', N'AllocationState') IS NOT NULL
BEGIN
    DECLARE @DefaultConstraint SYSNAME;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.FiscalSeries')
          AND name = N'UX_FiscalSeries_Device')
        DROP INDEX [UX_FiscalSeries_Device] ON dbo.FiscalSeries;

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FiscalSeries')
          AND name = N'CK_FiscalSeries_AllocationState')
        ALTER TABLE dbo.FiscalSeries DROP CONSTRAINT [CK_FiscalSeries_AllocationState];

    SELECT @DefaultConstraint = constraint_object.name
    FROM sys.default_constraints constraint_object
    INNER JOIN sys.columns column_object
        ON column_object.object_id = constraint_object.parent_object_id
       AND column_object.column_id = constraint_object.parent_column_id
    WHERE constraint_object.parent_object_id = OBJECT_ID(N'dbo.FiscalSeries')
      AND column_object.name = N'AllocationState';

    IF @DefaultConstraint IS NOT NULL
    BEGIN
        DECLARE @DropConstraintSql NVARCHAR(MAX) =
            N'ALTER TABLE dbo.FiscalSeries DROP CONSTRAINT ' + QUOTENAME(@DefaultConstraint) + N';';
        EXEC sys.sp_executesql @DropConstraintSql;
    END;

    ALTER TABLE dbo.FiscalSeries DROP COLUMN AllocationState;
    PRINT N'RemoveFiscalSeriesAllocationState: estado del asignador de bloques retirado.';
END;

IF OBJECT_ID(N'dbo.FiscalSeries', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.FiscalSeries
    SET IsActive = 0
    WHERE IsActive = 1
      AND EmitterKind <> CASE WHEN DeviceId IS NULL THEN N'Server' ELSE N'Device' END;

    UPDATE dbo.FiscalSeries
    SET EmitterKind = CASE WHEN DeviceId IS NULL THEN N'Server' ELSE N'Device' END
    WHERE EmitterKind <> CASE WHEN DeviceId IS NULL THEN N'Server' ELSE N'Device' END;

    ;WITH RankedDeviceSeries AS (
        SELECT SeriesId,
               ROW_NUMBER() OVER (
                   PARTITION BY BusinessId, DeviceId, DocumentType
                   ORDER BY CreatedAt DESC, SeriesId DESC) AS Position
        FROM dbo.FiscalSeries
        WHERE EmitterKind = N'Device'
          AND DeviceId IS NOT NULL
          AND IsActive = 1)
    UPDATE fiscal_series
    SET IsActive = 0
    FROM dbo.FiscalSeries fiscal_series
    INNER JOIN RankedDeviceSeries ranked ON ranked.SeriesId = fiscal_series.SeriesId
    WHERE ranked.Position > 1;

    PRINT N'RemoveFiscalSeriesAllocationState: emisores existentes normalizados al modelo por dispositivo.';
END;
