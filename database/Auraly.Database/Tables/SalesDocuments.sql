CREATE TABLE [dbo].[SalesDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [SeriesId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [FiscalNumber] NVARCHAR(64) NOT NULL,
    [Prefix] NVARCHAR(16) NOT NULL,
    [Consecutive] BIGINT NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [CustomerIdentification] NVARCHAR(64) NOT NULL,
    [UntaxedAmount] DECIMAL(19, 4) NOT NULL,
    [TaxAmount] DECIMAL(19, 4) NOT NULL,
    [PayableAmount] DECIMAL(19, 4) NOT NULL,
    [CufeReceived] NVARCHAR(96) NOT NULL,
    [CufeCalculated] NVARCHAR(96) NULL,
    [FiscalStatus] NVARCHAR(40) NOT NULL,
    [ProcessingStatus] NVARCHAR(32) NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [CreatedByDeviceId] UNIQUEIDENTIFIER NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesDocuments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDocuments_BusinessLocations] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[BusinessLocations] ([LocationId]),
    CONSTRAINT [FK_SalesDocuments_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_SalesDocuments_CashRegisters] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_SalesDocuments_PosDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[PosDevices] ([DeviceId]),
    CONSTRAINT [FK_SalesDocuments_FiscalSeries] FOREIGN KEY ([SeriesId]) REFERENCES [dbo].[FiscalSeries] ([SeriesId]),
    CONSTRAINT [FK_SalesDocuments_FiscalAuthorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Document] UNIQUE ([BusinessId], [DocumentId]),
    CONSTRAINT [UQ_SalesDocuments_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [UQ_SalesDocuments_FiscalNumber] UNIQUE ([BusinessId], [DocumentType], [Prefix], [Consecutive]),
    CONSTRAINT [CK_SalesDocuments_Amounts] CHECK ([UntaxedAmount] >= 0 AND [TaxAmount] >= 0 AND [PayableAmount] >= 0)
);

GO

CREATE INDEX [IX_SalesDocuments_Business_Document]
    ON [dbo].[SalesDocuments] ([BusinessId], [DocumentId]);

GO

CREATE INDEX [IX_SalesDocuments_Business_Status_Received]
    ON [dbo].[SalesDocuments] ([BusinessId], [ProcessingStatus], [ReceivedAt]);

