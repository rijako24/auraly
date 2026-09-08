IF COL_LENGTH(N'dbo.PosSynchronizationOutboxMessages', N'EntityType') IS NULL
    ALTER TABLE dbo.PosSynchronizationOutboxMessages ADD EntityType NVARCHAR(32) NULL;
GO
IF COL_LENGTH(N'dbo.PosSynchronizationOutboxMessages', N'EntityId') IS NULL
    ALTER TABLE dbo.PosSynchronizationOutboxMessages ADD EntityId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.PosSynchronizationOutboxMessages', N'ChangeKind') IS NULL
    ALTER TABLE dbo.PosSynchronizationOutboxMessages ADD ChangeKind NVARCHAR(16) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_PosSynchronizationOutboxMessages_ChangeKind')
    ALTER TABLE dbo.PosSynchronizationOutboxMessages WITH CHECK
      ADD CONSTRAINT CK_PosSynchronizationOutboxMessages_ChangeKind
      CHECK (ChangeKind IS NULL OR ChangeKind IN (N'Upsert',N'Tombstone'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PosSynchronizationOutboxMessages') AND name=N'IX_PosSynchronizationOutboxMessages_Changes')
    CREATE INDEX IX_PosSynchronizationOutboxMessages_Changes
      ON dbo.PosSynchronizationOutboxMessages(BusinessId,Stream,AvailableThroughCursor)
      INCLUDE(EntityType,EntityId,ChangeKind);
GO
