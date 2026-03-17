CREATE TABLE [dbo].[ServiceResourceUsages] (
    [ServiceResourceUsageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ServiceId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessResourceId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] INT NOT NULL,
    CONSTRAINT [FK_ServiceResourceUsages_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ServiceResourceUsages_BusinessResources] FOREIGN KEY ([BusinessResourceId])
        REFERENCES [dbo].[BusinessResources] ([BusinessResourceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ServiceResourceUsages_ServiceId_BusinessResourceId] ON [dbo].[ServiceResourceUsages] ([ServiceId], [BusinessResourceId]);

GO

CREATE INDEX [IX_ServiceResourceUsages_ServiceId] ON [dbo].[ServiceResourceUsages] ([ServiceId]);

GO

CREATE INDEX [IX_ServiceResourceUsages_BusinessResourceId] ON [dbo].[ServiceResourceUsages] ([BusinessResourceId]);

GO
