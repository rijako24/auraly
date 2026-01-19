CREATE TABLE [dbo].[Leads] (
    [LeadId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserNumber] NVARCHAR(50) NOT NULL,
    [BabyAge] INT NULL,
    [RecommendedPlan] NVARCHAR(100) NULL,
    [Status] NVARCHAR(20) NOT NULL DEFAULT 'New',
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CustomerName] NVARCHAR(100) NULL,
    [Notes] NVARCHAR(1000) NULL
);

GO

CREATE INDEX [IX_Leads_UserNumber] ON [dbo].[Leads] ([UserNumber]);

GO
