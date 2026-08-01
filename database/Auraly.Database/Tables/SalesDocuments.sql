CREATE TABLE [dbo].[SalesDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [SourceMode] NVARCHAR(16) NOT NULL CONSTRAINT [DF_SalesDocuments_SourceMode] DEFAULT N'PosEdge',
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [FiscalSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [FiscalNumber] NVARCHAR(64) NOT NULL,
    [FiscalPrefix] NVARCHAR(16) NOT NULL,
    [FiscalConsecutive] BIGINT NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [CustomerIdentification] NVARCHAR(64) NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [UntaxedAmount] DECIMAL(19, 4) NOT NULL,
    [TaxAmount] DECIMAL(19, 4) NOT NULL,
    [PayableAmount] DECIMAL(19, 4) NOT NULL,
    [CufeReceived] NVARCHAR(96) NOT NULL,
    [CufeCalculated] NVARCHAR(96) NULL,
    [FiscalStatus] NVARCHAR(40) NOT NULL,
    [ProcessingStatus] NVARCHAR(32) NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [CreatedByDeviceId] UNIQUEIDENTIFIER NULL,
    [SoldByUserId] UNIQUEIDENTIFIER NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [CashSessionId] UNIQUEIDENTIFIER NULL,
    [CashierShiftId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesDocuments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDocuments_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_SalesDocuments_CashRegisters] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_SalesDocuments_PosDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[PosDevices] ([DeviceId]),
    CONSTRAINT [FK_SalesDocuments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SalesDocuments_FiscalSeries] FOREIGN KEY ([FiscalSeriesId]) REFERENCES [dbo].[FiscalSeries] ([SeriesId]),
    CONSTRAINT [FK_SalesDocuments_FiscalAuthorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [FK_SalesDocuments_SoldByUser] FOREIGN KEY ([SoldByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SalesDocuments_WorkSession] FOREIGN KEY ([WorkSessionId])
        REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SalesDocuments_CashSession] FOREIGN KEY ([CashSessionId]) REFERENCES [dbo].[CashSessions] ([CashSessionId]),
    CONSTRAINT [FK_SalesDocuments_CashierShift]
        FOREIGN KEY ([CashSessionId],[CashierShiftId])
        REFERENCES [dbo].[CashierShifts] ([CashSessionId],[CashierShiftId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Document] UNIQUE ([BusinessId], [DocumentId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [FK_SalesDocuments_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [UQ_SalesDocuments_AuralyNumber]
        UNIQUE ([BusinessId], [DocumentType], [DocumentPrefix], [DocumentSeriesCode], [DocumentConsecutive]),
    CONSTRAINT [UQ_SalesDocuments_FiscalNumber]
        UNIQUE ([BusinessId], [DocumentType], [FiscalAuthorizationId], [FiscalPrefix], [FiscalConsecutive]),
    CONSTRAINT [CK_SalesDocuments_Amounts] CHECK ([UntaxedAmount] >= 0 AND [TaxAmount] >= 0 AND [PayableAmount] >= 0),
    CONSTRAINT [CK_SalesDocuments_SourceMode] CHECK ([SourceMode] IN (N'PosEdge', N'Online'))
);

GO

CREATE INDEX [IX_SalesDocuments_Business_Document]
    ON [dbo].[SalesDocuments] ([BusinessId], [DocumentId]);

GO

CREATE INDEX [IX_SalesDocuments_Business_Status_Received]
    ON [dbo].[SalesDocuments] ([BusinessId], [ProcessingStatus], [ReceivedAt]);

GO

CREATE INDEX [IX_SalesDocuments_Business_Customer_Issued]
    ON [dbo].[SalesDocuments] ([BusinessId], [CustomerId], [IssuedAt])
    WHERE [CustomerId] IS NOT NULL;

GO

CREATE INDEX [IX_SalesDocuments_Register_Cashier_Issued]
    ON [dbo].[SalesDocuments] ([RegisterId],[CashSessionId],[CashierShiftId],[SoldByUserId],[IssuedAt]);


GO

CREATE INDEX [IX_SalesDocuments_WorkSession_Issued]
    ON [dbo].[SalesDocuments] ([WorkSessionId],[IssuedAt]);
GO
