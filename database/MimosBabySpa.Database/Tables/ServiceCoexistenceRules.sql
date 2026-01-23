CREATE TABLE [dbo].[ServiceCoexistenceRules] (
    [ServiceCoexistenceRuleId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceId1] UNIQUEIDENTIFIER NOT NULL,
    [ServiceId2] UNIQUEIDENTIFIER NOT NULL,
    [CanCoexist] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_ServiceCoexistenceRules_Businesses] FOREIGN KEY ([BusinessId]) 
        REFERENCES [dbo].[Businesses] ([BusinessId]) 
        ON DELETE RESTRICT,
    CONSTRAINT [FK_ServiceCoexistenceRules_Services1] FOREIGN KEY ([ServiceId1]) 
        REFERENCES [dbo].[Services] ([ServiceId]) 
        ON DELETE RESTRICT,
    CONSTRAINT [FK_ServiceCoexistenceRules_Services2] FOREIGN KEY ([ServiceId2]) 
        REFERENCES [dbo].[Services] ([ServiceId]) 
        ON DELETE RESTRICT
);

GO

CREATE UNIQUE INDEX [IX_ServiceCoexistenceRules_BusinessId_ServiceId1_ServiceId2] 
    ON [dbo].[ServiceCoexistenceRules] ([BusinessId], [ServiceId1], [ServiceId2]);

GO

CREATE INDEX [IX_ServiceCoexistenceRules_BusinessId] 
    ON [dbo].[ServiceCoexistenceRules] ([BusinessId]);

GO

CREATE INDEX [IX_ServiceCoexistenceRules_ServiceId1] 
    ON [dbo].[ServiceCoexistenceRules] ([ServiceId1]);

GO

CREATE INDEX [IX_ServiceCoexistenceRules_ServiceId2] 
    ON [dbo].[ServiceCoexistenceRules] ([ServiceId2]);

GO
