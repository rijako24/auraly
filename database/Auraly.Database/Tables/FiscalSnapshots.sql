CREATE TABLE [dbo].[FiscalSnapshots]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [TechnicalKeyVersion] NVARCHAR(64) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [CufeReceived] NVARCHAR(96) NOT NULL,
    [CufeCalculated] NVARCHAR(96) NULL,
    [QrPayload] NVARCHAR(MAX) NOT NULL,
    [IntegrityStatus] NVARCHAR(40) NOT NULL,
    [VerifiedAt] DATETIMEOFFSET(7) NULL,
    [ConflictReason] NVARCHAR(1000) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalSnapshots] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_FiscalSnapshots_FiscalDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [CK_FiscalSnapshots_Environment] CHECK ([Environment] IN (1, 2))
);

