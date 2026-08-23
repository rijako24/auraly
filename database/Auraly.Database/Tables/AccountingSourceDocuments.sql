CREATE TABLE [dbo].[AccountingSourceDocuments]
(
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_AccountingSourceDocuments]
        PRIMARY KEY CLUSTERED ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [FK_AccountingSourceDocuments_Tenants]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingSourceDocuments_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId])
);
GO

CREATE INDEX [IX_AccountingSourceDocuments_Business_Date]
    ON [dbo].[AccountingSourceDocuments]
       ([BusinessId],[OccurredAt],[SourceDocumentId]);
GO
