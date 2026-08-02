CREATE TABLE [dbo].[FiscalArtifacts]
(
    [FiscalArtifactId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [ArtifactType] NVARCHAR(48) NOT NULL,
    [ArtifactVersion] INT NOT NULL,
    [Content] VARBINARY(MAX) NOT NULL,
    [ContentHash] BINARY(32) NOT NULL,
    [ContentType] NVARCHAR(128) NOT NULL,
    [FileName] NVARCHAR(256) NULL,
    [TechnicalAnnexVersion] NVARCHAR(32) NULL,
    [GeneratorVersion] NVARCHAR(64) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalArtifacts] PRIMARY KEY CLUSTERED ([FiscalArtifactId]),
    CONSTRAINT [FK_FiscalArtifacts_FiscalDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [UQ_FiscalArtifacts_Document_Type_Version] UNIQUE ([DocumentId], [ArtifactType], [ArtifactVersion]),
    CONSTRAINT [CK_FiscalArtifacts_Version] CHECK ([ArtifactVersion] > 0),
    CONSTRAINT [CK_FiscalArtifacts_Content] CHECK (DATALENGTH([Content]) > 0)
);

GO

CREATE INDEX [IX_FiscalArtifacts_Document_Type]
    ON [dbo].[FiscalArtifacts] ([DocumentId], [ArtifactType]);