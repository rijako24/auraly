CREATE TABLE [dbo].[DocumentSeries]
(
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [Prefix] NVARCHAR(8) NOT NULL,
    [SeriesCode] NVARCHAR(16) NOT NULL,
    [Padding] TINYINT NOT NULL,
    [RangeStart] BIGINT NOT NULL,
    [RangeEnd] BIGINT NOT NULL,
    [IsOfflineCapable] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DocumentSeries] PRIMARY KEY CLUSTERED ([DocumentSeriesId]),
    CONSTRAINT [FK_DocumentSeries_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DocumentSeries_CashRegisters] FOREIGN KEY ([RegisterId])
        REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [UQ_DocumentSeries_Business_Type_Code]
        UNIQUE ([BusinessId], [DocumentType], [Prefix], [SeriesCode]),
    CONSTRAINT [CK_DocumentSeries_Padding] CHECK ([Padding] = 8),
    CONSTRAINT [CK_DocumentSeries_Range] CHECK ([RangeStart] > 0 AND [RangeEnd] >= [RangeStart] AND [RangeEnd] <= 99999999)
);

GO

CREATE INDEX [IX_DocumentSeries_Business_Type_Active]
    ON [dbo].[DocumentSeries] ([BusinessId], [DocumentType], [IsActive]);

GO

CREATE INDEX [IX_DocumentSeries_Register_Type]
    ON [dbo].[DocumentSeries] ([RegisterId], [DocumentType])
    WHERE [RegisterId] IS NOT NULL;
