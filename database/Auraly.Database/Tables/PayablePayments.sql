CREATE TABLE [dbo].[SupplierPayments]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [PaidAt] DATETIMEOFFSET(7) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [PaymentMethod] NVARCHAR(24) NOT NULL,
    [Reference] NVARCHAR(120) NULL,
    [Notes] NVARCHAR(1000) NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SupplierPayments] PRIMARY KEY CLUSTERED ([PaymentId]),
    CONSTRAINT [FK_SupplierPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierPayments_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierPayments_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SupplierPayments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SupplierPayments_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_SupplierPayments_Business_Number] UNIQUE ([BusinessId], [DocumentNumber]),
    CONSTRAINT [UQ_SupplierPayments_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_SupplierPayments_Total] CHECK ([TotalAmount] > 0),
    CONSTRAINT [CK_SupplierPayments_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_SupplierPayments_Method] CHECK ([PaymentMethod] IN (N'Cash', N'BankTransfer')),
    CONSTRAINT [CK_SupplierPayments_Status] CHECK ([Status] IN (N'Accepted', N'Processed'))
);
GO
CREATE INDEX [IX_SupplierPayments_Business_Paid]
    ON [dbo].[SupplierPayments] ([BusinessId], [PaidAt] DESC)
    INCLUDE ([SupplierId], [Status], [TotalAmount], [PaymentMethod]);
GO

CREATE TABLE [dbo].[SupplierPaymentApplications]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [AppliedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_SupplierPaymentApplications] PRIMARY KEY CLUSTERED ([PaymentId], [LineNumber]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[SupplierPayments] ([PaymentId]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payable] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [UQ_SupplierPaymentApplications_Payable] UNIQUE ([PaymentId], [PayableId]),
    CONSTRAINT [CK_SupplierPaymentApplications_Line] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_SupplierPaymentApplications_Amount] CHECK ([Amount] > 0)
);
GO
CREATE INDEX [IX_SupplierPaymentApplications_Pending]
    ON [dbo].[SupplierPaymentApplications] ([PayableId], [AppliedAt])
    INCLUDE ([PaymentId], [Amount]);
GO
