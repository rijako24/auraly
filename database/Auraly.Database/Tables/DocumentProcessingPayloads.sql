CREATE TABLE [dbo].[DocumentProcessingPayloads]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ContractVersion] SMALLINT NOT NULL CONSTRAINT [DF_DocumentProcessingPayloads_Version] DEFAULT (1),
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DocumentProcessingPayloads]
        PRIMARY KEY CLUSTERED ([DocumentId], [DocumentType]),
    CONSTRAINT [FK_DocumentProcessingPayloads_Jobs]
        FOREIGN KEY ([DocumentId], [DocumentType])
        REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId], [DocumentType]),
    CONSTRAINT [FK_DocumentProcessingPayloads_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_DocumentProcessingPayloads_Version]
        CHECK ([ContractVersion] > 0),
    CONSTRAINT [CK_DocumentProcessingPayloads_Json]
        CHECK (ISJSON([PayloadJson]) = 1)
);

GO

CREATE INDEX [IX_DocumentProcessingPayloads_Business]
    ON [dbo].[DocumentProcessingPayloads] ([BusinessId], [AcceptedAt])
    INCLUDE ([DocumentId], [DocumentType], [ContractVersion]);

