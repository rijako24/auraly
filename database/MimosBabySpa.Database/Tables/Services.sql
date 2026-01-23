CREATE TABLE [dbo].[Services] (
    [ServiceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceName] NVARCHAR(200) NOT NULL,
    [DurationMinutes] INT NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Services_Businesses] FOREIGN KEY ([BusinessId]) 
        REFERENCES [dbo].[Businesses] ([BusinessId]) 
        ON DELETE RESTRICT
);

GO

CREATE UNIQUE INDEX [IX_Services_BusinessId_ServiceName] 
    ON [dbo].[Services] ([BusinessId], [ServiceName]);

GO

CREATE INDEX [IX_Services_BusinessId] 
    ON [dbo].[Services] ([BusinessId]);

GO
