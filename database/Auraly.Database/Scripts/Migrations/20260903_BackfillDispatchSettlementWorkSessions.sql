SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DispatchSettlementOperations', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.DispatchSettlementOperations', N'WorkSessionId') IS NULL
    ALTER TABLE dbo.DispatchSettlementOperations
        ADD WorkSessionId UNIQUEIDENTIFIER NULL;

IF OBJECT_ID(N'dbo.DispatchSettlementOperations', N'U') IS NOT NULL
BEGIN
    -- Dynamic SQL keeps this migration executable both before and after the
    -- DACPAC has introduced WorkSessionId; SQL Server otherwise binds the
    -- newly added column before executing the guarded ALTER above.
    EXEC sys.sp_executesql N'
        UPDATE operation
        SET WorkSessionId = existing.WorkSessionId
        FROM dbo.DispatchSettlementOperations operation
        INNER JOIN dbo.Businesses business ON business.BusinessId=operation.BusinessId
        OUTER APPLY
        (
            SELECT TOP (1) session.WorkSessionId
            FROM dbo.WorkSessions session
            WHERE session.TenantId=business.TenantId
              AND session.BusinessId=operation.BusinessId
              AND session.UserId=operation.RequestedBy
            ORDER BY
                CASE WHEN session.Status=N''Open'' THEN 0 ELSE 1 END,
                CASE WHEN session.OpenedAt<=operation.RequestedAt THEN 0 ELSE 1 END,
                session.OpenedAt DESC,
                session.WorkSessionId
        ) existing
        WHERE operation.WorkSessionId IS NULL
          AND existing.WorkSessionId IS NOT NULL;

        DECLARE @FallbackSessions TABLE
        (
            DispatchSettlementOperationId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            WorkSessionId UNIQUEIDENTIFIER NOT NULL UNIQUE
        );

        INSERT @FallbackSessions(DispatchSettlementOperationId,WorkSessionId)
        SELECT operation.DispatchSettlementOperationId,NEWID()
        FROM dbo.DispatchSettlementOperations operation
        WHERE operation.WorkSessionId IS NULL;

        INSERT dbo.WorkSessions
        (
            WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
            OpenedAt,LastActivityAt,ClosedAt,Status
        )
        SELECT fallback.WorkSessionId,business.TenantId,operation.BusinessId,
               dispatch.WarehouseId,operation.RequestedBy,NULL,
               operation.RequestedAt,operation.RequestedAt,operation.RequestedAt,N''Closed''
        FROM @FallbackSessions fallback
        INNER JOIN dbo.DispatchSettlementOperations operation
            ON operation.DispatchSettlementOperationId=fallback.DispatchSettlementOperationId
        INNER JOIN dbo.Businesses business ON business.BusinessId=operation.BusinessId
        INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=operation.DispatchId;

        UPDATE operation
        SET WorkSessionId=fallback.WorkSessionId
        FROM dbo.DispatchSettlementOperations operation
        INNER JOIN @FallbackSessions fallback
            ON fallback.DispatchSettlementOperationId=operation.DispatchSettlementOperationId;

        IF EXISTS (SELECT 1 FROM dbo.DispatchSettlementOperations WHERE WorkSessionId IS NULL)
            THROW 51000,N''No fue posible asociar todas las liquidaciones históricas a una sesión operativa.'',1;

        ALTER TABLE dbo.DispatchSettlementOperations
            ALTER COLUMN WorkSessionId UNIQUEIDENTIFIER NOT NULL;';
END;

COMMIT TRANSACTION;
