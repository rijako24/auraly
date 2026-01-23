CREATE TABLE [dbo].[BusinessResources] (
    [BusinessResourceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ResourceName] NVARCHAR(100) NOT NULL,
    [Quantity] INT NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_BusinessResources_Businesses] FOREIGN KEY ([BusinessId]) 
        REFERENCES [dbo].[Businesses] ([BusinessId]) 
        ON DELETE RESTRICT
);

GO

CREATE UNIQUE INDEX [IX_BusinessResources_BusinessId_ResourceName] 
    ON [dbo].[BusinessResources] ([BusinessId], [ResourceName]);

GO

CREATE INDEX [IX_BusinessResources_BusinessId] 
    ON [dbo].[BusinessResources] ([BusinessId]);

GO
