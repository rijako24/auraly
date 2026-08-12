IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AuditLogs', N'Action') IS NOT NULL
BEGIN
    ALTER TABLE dbo.AuditLogs ALTER COLUMN Action NVARCHAR(300) NOT NULL;
    PRINT 'ExpandAuditAction: AuditLogs.Action expanded to 300 characters.';
END;
ELSE
    PRINT 'ExpandAuditAction: AuditLogs table not found; skipped.';
GO
