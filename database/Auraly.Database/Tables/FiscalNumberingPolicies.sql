CREATE TABLE [fiscal].[FiscalNumberingPolicies]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [BlockSize] BIGINT NOT NULL CONSTRAINT [DF_FiscalNumberingPolicies_BlockSize] DEFAULT (100),
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalNumberingPolicies] PRIMARY KEY CLUSTERED ([BusinessId], [DocumentType]),
    CONSTRAINT [FK_FiscalNumberingPolicies_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_FiscalNumberingPolicies_DocumentType] CHECK ([DocumentType] IN (N'SalesInvoice')),
    CONSTRAINT [CK_FiscalNumberingPolicies_Values] CHECK ([BlockSize] > 0)
);
GO
