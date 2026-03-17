CREATE TABLE [dbo].[ServiceCategories] (
    [ServiceCategoryId]   UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId]          UNIQUEIDENTIFIER NOT NULL,
    [Name]                NVARCHAR(100)    NOT NULL,
    [DisplayOrder]        INT              NOT NULL DEFAULT 0,
    [IsActive]            BIT              NOT NULL DEFAULT 1,
    [CreatedAt]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]           DATETIME2        NULL,
    CONSTRAINT [FK_ServiceCategories_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ServiceCategories_BusinessId_Name] 
    ON [dbo].[ServiceCategories] ([BusinessId], [Name]);

GO

CREATE INDEX [IX_ServiceCategories_BusinessId] ON [dbo].[ServiceCategories] ([BusinessId]);

GO
