CREATE TABLE [dbo].[AuditLogs] (
    [AuditLogId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [TenantId] UNIQUEIDENTIFIER NULL,
    [BusinessId] UNIQUEIDENTIFIER NULL,
    [Action] NVARCHAR(300) NOT NULL,
    [EntityType] NVARCHAR(100) NOT NULL,
    [EntityId] NVARCHAR(100) NULL,
    [OldValues] NVARCHAR(MAX) NULL,
    [NewValues] NVARCHAR(MAX) NULL,
    [IpAddress] NVARCHAR(50) NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [CorrelationId] NVARCHAR(100) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_AuditLogs_AppUsers] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE SET NULL,
    CONSTRAINT [FK_AuditLogs_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE SET NULL
);

GO

CREATE INDEX [IX_AuditLogs_UserId] ON [dbo].[AuditLogs] ([UserId]);

GO

CREATE INDEX [IX_AuditLogs_TenantId_Timestamp] ON [dbo].[AuditLogs] ([TenantId], [Timestamp]);

GO

CREATE INDEX [IX_AuditLogs_CorrelationId] ON [dbo].[AuditLogs] ([CorrelationId]);

GO
