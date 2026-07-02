CREATE TABLE [dbo].[Services] (
    [ServiceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceName] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    [Keywords] NVARCHAR(1000) NULL,
    [DurationMinutes] INT NOT NULL,
    [Price] DECIMAL(18, 2) NOT NULL,
    [IncludeInCheckoutTotal] BIT NOT NULL DEFAULT 1,
    [CategoryId] UNIQUEIDENTIFIER NOT NULL,
    [Tier] INT NOT NULL DEFAULT 0,
    [ServiceType] INT NOT NULL DEFAULT 0,
    [FulfillmentKind] INT NOT NULL DEFAULT 0,
    [FixedScheduleLabel] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Services_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Services_ServiceCategories] FOREIGN KEY ([CategoryId])
        REFERENCES [dbo].[ServiceCategories] ([ServiceCategoryId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_Services_BusinessId_ServiceName] ON [dbo].[Services] ([BusinessId], [ServiceName]);

GO

CREATE INDEX [IX_Services_BusinessId] ON [dbo].[Services] ([BusinessId]);

GO

CREATE INDEX [IX_Services_BusinessId_CategoryId] ON [dbo].[Services] ([BusinessId], [CategoryId]);

GO
