CREATE TABLE [dbo].[Leads] (
    [LeadId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [UserNumber] NVARCHAR(50) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [CustomerName] NVARCHAR(100) NULL,
    [Notes] NVARCHAR(1000) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_Leads_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Leads_BusinessId_UserNumber] ON [dbo].[Leads] ([BusinessId], [UserNumber]);

GO
