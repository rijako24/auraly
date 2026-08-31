CREATE TABLE [dbo].[SalesDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [SourceMode] NVARCHAR(16) NOT NULL CONSTRAINT [DF_SalesDocuments_SourceMode] DEFAULT N'PosEdge',
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [FiscalSeriesId] UNIQUEIDENTIFIER NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [RequestHash] BINARY(32) NULL,
    [FiscalNumber] NVARCHAR(64) NULL,
    [FiscalPrefix] NVARCHAR(16) NULL,
    [FiscalConsecutive] BIGINT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [CustomerIdentification] NVARCHAR(64) NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [UntaxedAmount] DECIMAL(19, 4) NOT NULL,
    [TaxAmount] DECIMAL(19, 4) NOT NULL,
    [PayableAmount] DECIMAL(19, 4) NOT NULL,
    [CreditAmount] DECIMAL(19, 4) NOT NULL CONSTRAINT [DF_SalesDocuments_CreditAmount] DEFAULT 0,
    [CreditDueDate] DATETIMEOFFSET(7) NULL,
    [CufeReceived] NVARCHAR(96) NULL,
    [CufeCalculated] NVARCHAR(96) NULL,
    [FiscalStatus] NVARCHAR(40) NULL,
    [ProcessingStatus] NVARCHAR(32) NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [CreatedByDeviceId] UNIQUEIDENTIFIER NULL,
    [SoldByUserId] UNIQUEIDENTIFIER NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesDocuments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDocuments_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_SalesDocuments_EnrolledDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_SalesDocuments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SalesDocuments_FiscalSeries] FOREIGN KEY ([FiscalSeriesId]) REFERENCES [dbo].[FiscalSeries] ([SeriesId]),
    CONSTRAINT [FK_SalesDocuments_FiscalAuthorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [FK_SalesDocuments_SoldByUser] FOREIGN KEY ([SoldByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SalesDocuments_WorkSession] FOREIGN KEY ([WorkSessionId])
        REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Document] UNIQUE ([BusinessId], [DocumentId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [FK_SalesDocuments_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [UQ_SalesDocuments_AuralyNumber]
        UNIQUE ([BusinessId], [DocumentType], [DocumentPrefix], [DocumentSeriesCode], [DocumentConsecutive]),
    CONSTRAINT [CK_SalesDocuments_DocumentType] CHECK ([DocumentType] IN (N'SalesInvoice',N'SalesReceipt',N'ServiceInvoice')),
    CONSTRAINT [CK_SalesDocuments_FiscalShape] CHECK (
      ([DocumentType] IN(N'SalesInvoice',N'ServiceInvoice') AND [FiscalSeriesId] IS NOT NULL AND [FiscalAuthorizationId] IS NOT NULL AND [FiscalNumber] IS NOT NULL AND [FiscalPrefix] IS NOT NULL AND [FiscalConsecutive] IS NOT NULL AND [CufeReceived] IS NOT NULL AND [FiscalStatus] IS NOT NULL)
      OR
      ([DocumentType]=N'SalesReceipt' AND [FiscalSeriesId] IS NULL AND [FiscalAuthorizationId] IS NULL AND [FiscalNumber] IS NULL AND [FiscalPrefix] IS NULL AND [FiscalConsecutive] IS NULL AND [CufeReceived] IS NULL AND [CufeCalculated] IS NULL AND [FiscalStatus] IS NULL)),
    CONSTRAINT [CK_SalesDocuments_Amounts] CHECK ([UntaxedAmount] >= 0 AND [TaxAmount] >= 0 AND [PayableAmount] >= 0 AND [CreditAmount] BETWEEN 0 AND [PayableAmount]),
    CONSTRAINT [CK_SalesDocuments_CreditTerms] CHECK (([CreditAmount] = 0 AND [CreditDueDate] IS NULL) OR ([CreditAmount] > 0 AND [CreditDueDate] IS NOT NULL AND [CustomerId] IS NOT NULL)),
    CONSTRAINT [CK_SalesDocuments_SourceMode] CHECK ([SourceMode] IN (N'PosEdge', N'Online')),
    CONSTRAINT [CK_SalesDocuments_OperationalShape] CHECK
      (([DocumentType]=N'ServiceInvoice' AND [SourceMode]=N'Online' AND [WarehouseId] IS NULL
        AND [DeviceId] IS NULL AND [CreatedByDeviceId] IS NULL AND [WorkSessionId] IS NULL)
       OR
       ([DocumentType] IN(N'SalesInvoice',N'SalesReceipt') AND [WarehouseId] IS NOT NULL))
);

GO

CREATE INDEX [IX_SalesDocuments_Business_Document]
    ON [dbo].[SalesDocuments] ([BusinessId], [DocumentId]);

GO

CREATE INDEX [IX_SalesDocuments_Business_Status_Received]
    ON [dbo].[SalesDocuments] ([BusinessId], [ProcessingStatus], [ReceivedAt]);

GO
CREATE UNIQUE INDEX [UX_SalesDocuments_FiscalNumber]
    ON [dbo].[SalesDocuments] ([BusinessId],[DocumentType],[FiscalAuthorizationId],[FiscalPrefix],[FiscalConsecutive])
    WHERE [FiscalAuthorizationId] IS NOT NULL;
GO


CREATE INDEX [IX_SalesDocuments_Business_Customer_Issued]
    ON [dbo].[SalesDocuments] ([BusinessId], [CustomerId], [IssuedAt])
    WHERE [CustomerId] IS NOT NULL;

GO

CREATE INDEX [IX_SalesDocuments_WorkSession_Issued]
    ON [dbo].[SalesDocuments] ([WorkSessionId],[IssuedAt]);
GO
