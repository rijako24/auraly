CREATE TABLE [dbo].[BusinessAttachments] (
    [BusinessAttachmentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId]           UNIQUEIDENTIFIER NOT NULL,
    [BlobPath]            NVARCHAR(500)   NOT NULL,
    [MediaType]           NVARCHAR(50)    NOT NULL DEFAULT 'document',
    [Filename]            NVARCHAR(200)   NULL,
    [Description]         NVARCHAR(500)   NULL,
    [IsActive]            BIT             NOT NULL DEFAULT 1,
    [CreatedAt]           DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_BusinessAttachments_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_BusinessAttachments_BusinessId] ON [dbo].[BusinessAttachments] ([BusinessId]);

GO
