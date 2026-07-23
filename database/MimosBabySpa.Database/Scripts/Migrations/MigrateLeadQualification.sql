-- Optional, derived commercial projection of the agent's current flow stage.
IF COL_LENGTH('dbo.Leads', 'QualificationBand') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationBand NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.Leads', 'QualificationLabel') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationLabel NVARCHAR(160) NULL;
IF COL_LENGTH('dbo.Leads', 'QualificationPriority') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationPriority INT NULL;
IF COL_LENGTH('dbo.Leads', 'QualificationFlowId') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationFlowId NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Leads', 'QualificationStageId') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationStageId NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Leads', 'QualificationUpdatedAt') IS NULL
    ALTER TABLE dbo.Leads ADD QualificationUpdatedAt DATETIME2 NULL;
IF COL_LENGTH('dbo.Leads', 'ConvertedAt') IS NULL
    ALTER TABLE dbo.Leads ADD ConvertedAt DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_BusinessId_QualificationPriority')
    CREATE INDEX IX_Leads_BusinessId_QualificationPriority
        ON dbo.Leads (BusinessId, QualificationPriority DESC, QualificationUpdatedAt DESC);
GO
