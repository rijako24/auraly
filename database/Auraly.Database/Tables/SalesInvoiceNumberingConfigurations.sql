CREATE TABLE [dbo].[SalesInvoiceNumberingConfigurations]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [InitialConsecutive] BIGINT NOT NULL,
    [CreatedAt] DATETIMEOFFSET NOT NULL,
    [UpdatedAt] DATETIMEOFFSET NOT NULL,
    CONSTRAINT [PK_SalesInvoiceNumberingConfigurations] PRIMARY KEY ([BusinessId]),
    CONSTRAINT [FK_SalesInvoiceNumberingConfigurations_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesInvoiceNumberingConfigurations_InitialConsecutive]
        CHECK ([InitialConsecutive] >= 1)
);