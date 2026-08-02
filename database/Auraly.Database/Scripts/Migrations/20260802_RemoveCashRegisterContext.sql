/*
  One-way cutover from register/cash-session ownership to user work sessions
  and tenant-scoped enrolled devices. The transformation runs only when the
  previous canonical PosDevices table exists. It is intentionally idempotent.
*/
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PosDevices', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (
            SELECT RegisterId
            FROM dbo.PosDevices
            WHERE IsActive=1
            GROUP BY RegisterId
            HAVING COUNT(*)>1)
            THROW 51200, ''More than one active enrolled device belongs to the same former register. Resolve the ambiguity before the Auraly cutover.'', 1;

        EXEC sys.sp_rename N''dbo.PosDevices'', N''EnrolledDevices'';

        IF COL_LENGTH(N''dbo.EnrolledDevices'', N''TenantId'') IS NULL
            ALTER TABLE dbo.EnrolledDevices ADD TenantId UNIQUEIDENTIFIER NULL;

        UPDATE d
        SET TenantId=b.TenantId
        FROM dbo.EnrolledDevices d
        JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
        WHERE d.TenantId IS NULL;

        IF EXISTS (SELECT 1 FROM dbo.EnrolledDevices WHERE TenantId IS NULL)
            THROW 51201, ''An enrolled device cannot be assigned to its tenant.'', 1;

        ALTER TABLE dbo.EnrolledDevices ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;

        /* Historical shifts become closed user work sessions. */
        IF OBJECT_ID(N''dbo.CashierShifts'', N''U'') IS NOT NULL
        BEGIN
            INSERT dbo.WorkSessions(
                WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                OpenedAt,LastActivityAt,ClosedAt,Status)
            SELECT
                sh.CashierShiftId,cs.BusinessId,r.WarehouseId,sh.UserId,
                d.DeviceId,sh.StartedAt,COALESCE(sh.EndedAt,SYSUTCDATETIME()),
                COALESCE(sh.EndedAt,SYSUTCDATETIME()),N''Closed''
            FROM dbo.CashierShifts sh
            JOIN dbo.CashSessions cs ON cs.CashSessionId=sh.CashSessionId
            JOIN dbo.CashRegisters r ON r.RegisterId=sh.RegisterId
            OUTER APPLY (
                SELECT TOP (1) ed.DeviceId
                FROM dbo.EnrolledDevices ed
                WHERE ed.RegisterId=sh.RegisterId
                ORDER BY ed.IsActive DESC,ed.CreatedAt DESC) d
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WorkSessions ws
                WHERE ws.WorkSessionId=sh.CashierShiftId);

            UPDATE sd
            SET WorkSessionId=sd.CashierShiftId
            FROM dbo.SalesDocuments sd
            WHERE sd.WorkSessionId IS NULL
              AND sd.CashierShiftId IS NOT NULL;
        END;

        /* A session without a shift still receives an auditable closed session. */
        IF OBJECT_ID(N''dbo.CashSessions'', N''U'') IS NOT NULL
        BEGIN
            INSERT dbo.WorkSessions(
                WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                OpenedAt,LastActivityAt,ClosedAt,Status)
            SELECT
                cs.CashSessionId,cs.BusinessId,r.WarehouseId,cs.OpenedByUserId,
                d.DeviceId,cs.OpenedAt,COALESCE(cs.ClosedAt,SYSUTCDATETIME()),
                COALESCE(cs.ClosedAt,SYSUTCDATETIME()),N''Closed''
            FROM dbo.CashSessions cs
            JOIN dbo.CashRegisters r ON r.RegisterId=cs.RegisterId
            OUTER APPLY (
                SELECT TOP (1) ed.DeviceId
                FROM dbo.EnrolledDevices ed
                WHERE ed.RegisterId=cs.RegisterId
                ORDER BY ed.IsActive DESC,ed.CreatedAt DESC) d
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.CashierShifts sh
                WHERE sh.CashSessionId=cs.CashSessionId)
              AND NOT EXISTS (
                SELECT 1 FROM dbo.WorkSessions ws
                WHERE ws.WorkSessionId=cs.CashSessionId);

            UPDATE sd
            SET WorkSessionId=sd.CashSessionId
            FROM dbo.SalesDocuments sd
            WHERE sd.WorkSessionId IS NULL
              AND sd.CashSessionId IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM dbo.WorkSessions ws
                  WHERE ws.WorkSessionId=sd.CashSessionId);
        END;

        /* Preserve every historical financial movement under its user session. */
        IF OBJECT_ID(N''dbo.CashMovements'', N''U'') IS NOT NULL
        BEGIN
            INSERT dbo.WorkSessionMovements(
                WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
                BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,
                SourceKey,OccurredAt,RecordedByUserId)
            SELECT
                m.CashMovementId,m.CashierShiftId,m.DocumentId,m.PaymentNumber,
                m.BusinessDate,m.MovementType,m.PaymentMethodCode,m.Amount,
                m.Reference,N''legacy-cash:''+CONVERT(nvarchar(36),m.CashMovementId),
                m.OccurredAt,m.RecordedByUserId
            FROM dbo.CashMovements m
            WHERE EXISTS (
                    SELECT 1 FROM dbo.WorkSessions ws
                    WHERE ws.WorkSessionId=m.CashierShiftId)
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.WorkSessionMovements wm
                    WHERE wm.WorkSessionMovementId=m.CashMovementId);
        END;

        /* Preserve the latest confirmed count for every historical user shift. */
        IF OBJECT_ID(N''dbo.CashCounts'', N''U'') IS NOT NULL
        BEGIN
            ;WITH Confirmed AS (
                SELECT c.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY c.CashierShiftId
                        ORDER BY c.ConfirmedAt DESC,c.CashCountId DESC) AS rn
                FROM dbo.CashCounts c
                WHERE c.Status=N''Confirmed'')
            INSERT dbo.WorkSessionClosures(
                WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
                TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,
                CountedCash,CashDifference,Note,ReceiptSnapshotJson,ReceiptHash,
                ClosedAt)
            SELECT
                c.CashCountId,c.CashierShiftId,c.CountedByUserId,
                N''legacy-cash:''+CONVERT(nvarchar(36),c.CashCountId),
                COALESCE(t.TotalSales,0),COALESCE(t.TotalRefunds,0),
                COALESCE(t.TotalOther,0),COALESCE(t.NetAmount,0),
                COALESCE(k.ExpectedCash,0),k.CountedCash,
                CASE WHEN k.CountedCash IS NULL THEN NULL
                     ELSE k.CountedCash-COALESCE(k.ExpectedCash,0) END,
                c.Observation,
                COALESCE(c.ReceiptSnapshotJson,N''{"source":"legacy-cash-count"}''),
                COALESCE(c.ReceiptHash,HASHBYTES(
                    ''SHA2_256'',CONVERT(varbinary(max),
                    COALESCE(c.ReceiptSnapshotJson,N''{"source":"legacy-cash-count"}'')))),
                c.ConfirmedAt
            FROM Confirmed c
            OUTER APPLY (
                SELECT
                    SUM(CASE WHEN m.MovementType=N''SalePayment'' THEN m.Amount ELSE 0 END) TotalSales,
                    ABS(SUM(CASE WHEN m.MovementType=N''Refund'' THEN m.Amount ELSE 0 END)) TotalRefunds,
                    SUM(CASE WHEN m.MovementType NOT IN (N''SalePayment'',N''Refund'') THEN m.Amount ELSE 0 END) TotalOther,
                    SUM(m.Amount) NetAmount
                FROM dbo.CashMovements m
                WHERE m.CashierShiftId=c.CashierShiftId) t
            OUTER APPLY (
                SELECT
                    SUM(CASE WHEN UPPER(l.PaymentMethodCode) LIKE N''%EFECT%''
                                  OR UPPER(l.PaymentMethodCode)=N''CASH''
                             THEN l.ExpectedAmount ELSE 0 END) ExpectedCash,
                    SUM(CASE WHEN UPPER(l.PaymentMethodCode) LIKE N''%EFECT%''
                                  OR UPPER(l.PaymentMethodCode)=N''CASH''
                             THEN l.CountedAmount ELSE 0 END) CountedCash
                FROM dbo.CashCountLines l
                WHERE l.CashCountId=c.CashCountId) k
            WHERE c.rn=1
              AND EXISTS (
                  SELECT 1 FROM dbo.WorkSessions ws
                  WHERE ws.WorkSessionId=c.CashierShiftId)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.WorkSessionClosures wc
                  WHERE wc.WorkSessionId=c.CashierShiftId);
        END;

        /* Existing active drafts become recoverable temporaries, never discarded. */
        IF COL_LENGTH(N''dbo.SalesDrafts'', N''WorkSessionId'') IS NULL
            ALTER TABLE dbo.SalesDrafts ADD WorkSessionId UNIQUEIDENTIFIER NULL;

        EXEC sys.sp_executesql N''
            INSERT dbo.WorkSessions(
                WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                OpenedAt,LastActivityAt,ClosedAt,Status)
            SELECT d.SalesDraftId,d.BusinessId,d.WarehouseId,d.UserId,ed.DeviceId,
                d.CreatedAt,d.UpdatedAt,d.UpdatedAt,N''''Closed''''
            FROM dbo.SalesDrafts d
            OUTER APPLY (
                SELECT TOP (1) p.DeviceId
                FROM dbo.EnrolledDevices p
                WHERE p.RegisterId=d.RegisterId
                ORDER BY p.IsActive DESC,p.CreatedAt DESC) ed
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WorkSessions ws
                WHERE ws.WorkSessionId=d.SalesDraftId);

            UPDATE dbo.SalesDrafts
            SET WorkSessionId=SalesDraftId,
                Status=CASE WHEN Status=N''''Active'''' THEN N''''Temporary'''' ELSE Status END,
                Name=CASE WHEN Status=N''''Active'''' AND Name IS NULL
                          THEN N''''Venta recuperada por actualización'''' ELSE Name END,
                SavedAt=CASE WHEN Status=N''''Active'''' AND SavedAt IS NULL
                             THEN UpdatedAt ELSE SavedAt END
            WHERE WorkSessionId IS NULL;'';
        ALTER TABLE dbo.SalesDrafts ALTER COLUMN WorkSessionId UNIQUEIDENTIFIER NOT NULL;

        /* Order claims are ephemeral; release them but retain their audit actor. */
        IF COL_LENGTH(N''dbo.OrderClaims'', N''WarehouseId'') IS NULL
            ALTER TABLE dbo.OrderClaims ADD WarehouseId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N''dbo.OrderClaims'', N''WorkSessionId'') IS NULL
            ALTER TABLE dbo.OrderClaims ADD WorkSessionId UNIQUEIDENTIFIER NULL;

        EXEC sys.sp_executesql N''
            UPDATE c
            SET WarehouseId=r.WarehouseId,WorkSessionId=c.OrderClaimId,
                ReleasedAt=COALESCE(c.ReleasedAt,SYSUTCDATETIME())
            FROM dbo.OrderClaims c
            JOIN dbo.CashRegisters r ON r.RegisterId=c.RegisterId;

            INSERT dbo.WorkSessions(
                WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                OpenedAt,LastActivityAt,ClosedAt,Status)
            SELECT c.OrderClaimId,c.BusinessId,c.WarehouseId,c.UserId,c.DeviceId,
                c.ClaimedAt,COALESCE(c.ReleasedAt,SYSUTCDATETIME()),
                COALESCE(c.ReleasedAt,SYSUTCDATETIME()),N''''Closed''''
            FROM dbo.OrderClaims c
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WorkSessions ws
                WHERE ws.WorkSessionId=c.OrderClaimId);'';
        ALTER TABLE dbo.OrderClaims ALTER COLUMN WarehouseId UNIQUEIDENTIFIER NOT NULL;
        ALTER TABLE dbo.OrderClaims ALTER COLUMN WorkSessionId UNIQUEIDENTIFIER NOT NULL;

        IF COL_LENGTH(N''dbo.OrderInvoiceBatchReceipts'', N''WarehouseId'') IS NULL
            ALTER TABLE dbo.OrderInvoiceBatchReceipts ADD WarehouseId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N''dbo.OrderInvoiceBatchReceipts'', N''WorkSessionId'') IS NULL
            ALTER TABLE dbo.OrderInvoiceBatchReceipts ADD WorkSessionId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N''dbo.OrderInvoiceBatchReceipts'', N''DeviceId'') IS NULL
            ALTER TABLE dbo.OrderInvoiceBatchReceipts ADD DeviceId UNIQUEIDENTIFIER NULL;

        EXEC sys.sp_executesql N''
            UPDATE b
            SET WarehouseId=r.WarehouseId,WorkSessionId=b.OperationId,DeviceId=d.DeviceId
            FROM dbo.OrderInvoiceBatchReceipts b
            JOIN dbo.CashRegisters r ON r.RegisterId=b.RegisterId
            OUTER APPLY (
                SELECT TOP (1) ed.DeviceId
                FROM dbo.EnrolledDevices ed
                WHERE ed.RegisterId=b.RegisterId
                ORDER BY ed.IsActive DESC,ed.CreatedAt DESC) d;

            INSERT dbo.WorkSessions(
                WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                OpenedAt,LastActivityAt,ClosedAt,Status)
            SELECT b.OperationId,b.BusinessId,b.WarehouseId,b.UserId,b.DeviceId,
                b.CreatedAt,b.UpdatedAt,b.UpdatedAt,N''''Closed''''
            FROM dbo.OrderInvoiceBatchReceipts b
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WorkSessions ws
                WHERE ws.WorkSessionId=b.OperationId);'';
        ALTER TABLE dbo.OrderInvoiceBatchReceipts ALTER COLUMN WarehouseId UNIQUEIDENTIFIER NOT NULL;
        ALTER TABLE dbo.OrderInvoiceBatchReceipts ALTER COLUMN WorkSessionId UNIQUEIDENTIFIER NOT NULL;

        /* Rebind operational and fiscal series to server or enrolled device. */
        IF COL_LENGTH(N''dbo.DocumentSeries'', N''DeviceId'') IS NULL
            ALTER TABLE dbo.DocumentSeries ADD DeviceId UNIQUEIDENTIFIER NULL;

        EXEC sys.sp_executesql N''
            UPDATE s
            SET DeviceId=d.DeviceId
            FROM dbo.DocumentSeries s
            OUTER APPLY (
                SELECT TOP (1) ed.DeviceId
                FROM dbo.EnrolledDevices ed
                WHERE ed.RegisterId=s.RegisterId AND ed.IsActive=1
                ORDER BY ed.CreatedAt DESC) d
            WHERE s.IsOfflineCapable=1;

            IF EXISTS (
                SELECT 1 FROM dbo.DocumentSeries
                WHERE IsOfflineCapable=1 AND IsActive=1 AND DeviceId IS NULL)
                THROW 51202, ''''An active offline document series has no enrolled device.'''', 1;'';
        ;WITH Ranked AS (
            SELECT DocumentSeriesId,
                ROW_NUMBER() OVER (
                    PARTITION BY BusinessId,DocumentType,Prefix
                    ORDER BY CASE WHEN SeriesCode=N''00'' THEN 0 ELSE 1 END,
                             CreatedAt DESC,DocumentSeriesId) rn
            FROM dbo.DocumentSeries
            WHERE IsOfflineCapable=0 AND IsActive=1)
        UPDATE s
        SET IsActive=CASE WHEN r.rn=1 THEN 1 ELSE 0 END,
            SeriesCode=CASE WHEN r.rn=1 THEN N''00'' ELSE s.SeriesCode END
        FROM dbo.DocumentSeries s
        JOIN Ranked r ON r.DocumentSeriesId=s.DocumentSeriesId;

        IF COL_LENGTH(N''dbo.FiscalSeries'', N''DeviceId'') IS NULL
            ALTER TABLE dbo.FiscalSeries ADD DeviceId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N''dbo.FiscalSeries'', N''EmitterKind'') IS NULL
            ALTER TABLE dbo.FiscalSeries ADD EmitterKind NVARCHAR(16) NULL;

        EXEC sys.sp_executesql N''
            IF EXISTS (
                SELECT 1
                FROM dbo.FiscalSeries fs
                WHERE EXISTS (SELECT 1 FROM dbo.SalesDocuments d WHERE d.FiscalSeriesId=fs.SeriesId AND d.SourceMode=N''''Online'''')
                  AND EXISTS (SELECT 1 FROM dbo.SalesDocuments d WHERE d.FiscalSeriesId=fs.SeriesId AND d.SourceMode=N''''PosEdge''''))
                THROW 51203, ''''A fiscal series was shared by online and offline issuers. Split it before the Auraly cutover.'''', 1;

            UPDATE fs
            SET EmitterKind=CASE
                    WHEN EXISTS (
                        SELECT 1 FROM dbo.SalesDocuments d
                        WHERE d.FiscalSeriesId=fs.SeriesId AND d.SourceMode=N''''Online'''')
                        THEN N''''Server''''
                    WHEN ed.DeviceId IS NOT NULL THEN N''''Device''''
                    ELSE N''''Server'''' END,
                DeviceId=CASE
                    WHEN EXISTS (
                        SELECT 1 FROM dbo.SalesDocuments d
                        WHERE d.FiscalSeriesId=fs.SeriesId AND d.SourceMode=N''''Online'''')
                        THEN NULL ELSE ed.DeviceId END
            FROM dbo.FiscalSeries fs
            OUTER APPLY (
                SELECT TOP (1) d.DeviceId
                FROM dbo.EnrolledDevices d
                WHERE d.RegisterId=fs.RegisterId AND d.IsActive=1
                ORDER BY d.CreatedAt DESC) ed;

            UPDATE dbo.FiscalSeries
            SET IsActive=0
            WHERE EmitterKind=N''''Device'''' AND DeviceId IS NULL;'';

        ALTER TABLE dbo.FiscalSeries ALTER COLUMN EmitterKind NVARCHAR(16) NOT NULL;

        /* Remove constraints and indexes that still own former register columns. */
        DECLARE @DropForeignKeys nvarchar(max)=N'''';
        SELECT @DropForeignKeys +=
            N''ALTER TABLE ''+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N''.''+
            QUOTENAME(OBJECT_NAME(parent_object_id))+N'' DROP CONSTRAINT ''+
            QUOTENAME(name)+N'';''
        FROM sys.foreign_keys fk
        WHERE fk.referenced_object_id IN (
            OBJECT_ID(N''dbo.CashRegisters''),OBJECT_ID(N''dbo.CashSessions''),
            OBJECT_ID(N''dbo.CashierShifts''))
           OR EXISTS (
                SELECT 1
                FROM sys.foreign_key_columns fkc
                WHERE fkc.constraint_object_id=fk.object_id
                  AND (
                    (fkc.parent_object_id=OBJECT_ID(N''dbo.EnrolledDevices'')
                     AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id) IN (N''BusinessId'',N''WarehouseId'',N''RegisterId''))
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.PosEnrollmentSessions'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.DocumentSeries'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.FiscalSeries'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.SalesDocuments'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id) IN (N''RegisterId'',N''CashSessionId'',N''CashierShiftId''))
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.SalesDrafts'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.OrderClaims'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')
                    OR (fkc.parent_object_id=OBJECT_ID(N''dbo.OrderInvoiceBatchReceipts'')
                        AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N''RegisterId'')));
        EXEC sys.sp_executesql @DropForeignKeys;

        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N''UQ_FiscalSeries_Register_Series'')
            ALTER TABLE dbo.FiscalSeries DROP CONSTRAINT UQ_FiscalSeries_Register_Series;

        DECLARE @DropIndexes nvarchar(max)=N'''';
        SELECT @DropIndexes += N''DROP INDEX ''+QUOTENAME(i.name)+N'' ON ''+
            QUOTENAME(OBJECT_SCHEMA_NAME(i.object_id))+N''.''+
            QUOTENAME(OBJECT_NAME(i.object_id))+N'';''
        FROM sys.indexes i
        WHERE i.name IN (
            N''IX_PosDevices_Business_Register'',N''IX_PosEnrollmentSessions_Register_Expiry'',
            N''IX_DocumentSeries_Register_Type'',N''IX_FiscalSeries_Business_Register'',
            N''IX_SalesDocuments_Register_Cashier_Issued'',N''UX_SalesDrafts_ActiveScope'',
            N''IX_OrderClaims_Business_Expires'');
        EXEC sys.sp_executesql @DropIndexes;

        ALTER TABLE dbo.EnrolledDevices DROP COLUMN BusinessId,WarehouseId,RegisterId;
        ALTER TABLE dbo.PosEnrollmentSessions DROP COLUMN RegisterId;
        ALTER TABLE dbo.DocumentSeries DROP COLUMN RegisterId;
        ALTER TABLE dbo.FiscalSeries DROP COLUMN RegisterId;
        ALTER TABLE dbo.SalesDocuments DROP COLUMN RegisterId,CashSessionId,CashierShiftId;
        ALTER TABLE dbo.SalesDrafts DROP COLUMN RegisterId;
        ALTER TABLE dbo.OrderClaims DROP COLUMN RegisterId;
        ALTER TABLE dbo.OrderInvoiceBatchReceipts DROP COLUMN RegisterId;

        /* Authorization grants were short-lived and register-bound; credentials survive. */
        DROP TABLE IF EXISTS dbo.SupervisorAuthorizationGrants;
        DROP TABLE IF EXISTS dbo.CashCountLines;
        DROP TABLE IF EXISTS dbo.CashCounts;
        DROP TABLE IF EXISTS dbo.CashMovements;
        DROP TABLE IF EXISTS dbo.CashCountNumberCursors;
        DROP TABLE IF EXISTS dbo.CashierShifts;
        DROP TABLE IF EXISTS dbo.CashSessions;
        DROP TABLE IF EXISTS dbo.CashRegisters;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    ';
END;
