CREATE TABLE [dbo].[FiscalSeries]
(
    [SeriesId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [Prefix] NVARCHAR(16) NOT NULL,
    [RangeStart] BIGINT NOT NULL,
    [RangeEnd] BIGINT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalSeries] PRIMARY KEY CLUSTERED ([SeriesId]),
    CONSTRAINT [FK_FiscalSeries_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalSeries_CashRegisters] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_FiscalSeries_FiscalAuthorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [UQ_FiscalSeries_Register_Series] UNIQUE ([RegisterId], [SeriesId]),
    CONSTRAINT [CK_FiscalSeries_Range] CHECK ([RangeStart] > 0 AND [RangeEnd] >= [RangeStart])
);

GO

CREATE INDEX [IX_FiscalSeries_Business_Register]
    ON [dbo].[FiscalSeries] ([BusinessId], [RegisterId]);

