CREATE TABLE [dbo].[Dispatches] (
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchNumber] NVARCHAR(64) NOT NULL,
    [ScheduledDate] DATE NOT NULL,
    [DriverName] NVARCHAR(160) NOT NULL,
    [VehiclePlate] NVARCHAR(24) NULL,
    [RouteId] UNIQUEIDENTIFIER NULL,
    [Notes] NVARCHAR(500) NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [PreparedAt] DATETIMEOFFSET(7) NULL,
    [VerificationStartedAt] DATETIMEOFFSET(7) NULL,
    [VerifiedAt] DATETIMEOFFSET(7) NULL,
    [ReleasedAt] DATETIMEOFFSET(7) NULL,
    [CancelledAt] DATETIMEOFFSET(7) NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Dispatches] PRIMARY KEY ([DispatchId]),
    CONSTRAINT [FK_Dispatches_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_Dispatches_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Dispatches_Warehouses] FOREIGN KEY ([BusinessId],[WarehouseId]) REFERENCES [dbo].[Warehouses] ([BusinessId],[WarehouseId]),
    CONSTRAINT [FK_Dispatches_Routes] FOREIGN KEY ([RouteId]) REFERENCES [dbo].[SalesRoutes] ([RouteId]),
    CONSTRAINT [FK_Dispatches_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_Dispatches_Business_Number] UNIQUE ([BusinessId],[DispatchNumber]),
    CONSTRAINT [CK_Dispatches_Status] CHECK ([Status] IN (N'Draft',N'Prepared',N'InVerification',N'Verified',N'Released',N'Cancelled'))
);
GO
CREATE INDEX [IX_Dispatches_Business_Date_Status] ON [dbo].[Dispatches] ([BusinessId],[ScheduledDate],[Status]) INCLUDE ([DriverName],[VehiclePlate],[UpdatedAt]);
GO

CREATE TABLE [dbo].[DispatchSourceDocuments] (
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(32) NOT NULL,
    [DocumentNumberSnapshot] NVARCHAR(64) NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [CustomerNameSnapshot] NVARCHAR(200) NOT NULL,
    [DeliveryAddressSnapshot] NVARCHAR(500) NULL,
    [SellerId] UNIQUEIDENTIFIER NULL,
    [SellerNameSnapshot] NVARCHAR(201) NOT NULL,
    [DocumentTotalSnapshot] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL CONSTRAINT [DF_DispatchSourceDocuments_Status] DEFAULT N'Pending',
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DispatchSourceDocuments] PRIMARY KEY ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchSourceDocuments_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchSourceDocuments_SalesDocuments] FOREIGN KEY ([SourceDocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_DispatchSourceDocuments_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_DispatchSourceDocuments_Sellers] FOREIGN KEY ([SellerId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchSourceDocuments_Dispatch_Document] UNIQUE ([DispatchId],[SourceDocumentId]),
    CONSTRAINT [CK_DispatchSourceDocuments_Type] CHECK ([SourceDocumentType] IN (N'SalesInvoice',N'SalesReceipt')),
    CONSTRAINT [CK_DispatchSourceDocuments_Status] CHECK ([Status] IN (N'Pending',N'Verified',N'Short',N'Released',N'Cancelled'))
);
GO
CREATE INDEX [IX_DispatchSourceDocuments_Source] ON [dbo].[DispatchSourceDocuments] ([SourceDocumentId],[DispatchId]);
GO

CREATE TABLE [dbo].[DispatchLines] (
    [DispatchLineId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCodeSnapshot] NVARCHAR(64) NOT NULL,
    [DescriptionSnapshot] NVARCHAR(300) NOT NULL,
    [AssignedQuantity] DECIMAL(19,6) NOT NULL,
    [VerifiedQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_DispatchLines_Verified] DEFAULT 0,
    [ShortageQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_DispatchLines_Shortage] DEFAULT 0,
    [UnitPriceSnapshot] DECIMAL(19,4) NOT NULL,
    [LineTotalSnapshot] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL CONSTRAINT [DF_DispatchLines_Status] DEFAULT N'Pending',
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DispatchLines] PRIMARY KEY ([DispatchLineId]),
    CONSTRAINT [FK_DispatchLines_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchLines_Documents] FOREIGN KEY ([DispatchSourceDocumentId]) REFERENCES [dbo].[DispatchSourceDocuments] ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_DispatchLines_Source] UNIQUE ([DispatchSourceDocumentId],[SourceLineNumber]),
    CONSTRAINT [CK_DispatchLines_Quantities] CHECK ([AssignedQuantity] > 0 AND [VerifiedQuantity] >= 0 AND [ShortageQuantity] >= 0 AND [VerifiedQuantity] + [ShortageQuantity] <= [AssignedQuantity]),
    CONSTRAINT [CK_DispatchLines_Status] CHECK ([Status] IN (N'Pending',N'PartiallyVerified',N'Verified',N'Short',N'Exception'))
);
GO
CREATE INDEX [IX_DispatchLines_Dispatch_Product] ON [dbo].[DispatchLines] ([DispatchId],[ProductId]);
GO

CREATE TABLE [dbo].[DispatchShortages] (
    [DispatchShortageId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchLineId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [Reason] NVARCHAR(120) NOT NULL,
    [Notes] NVARCHAR(500) NULL,
    [ResolutionStatus] NVARCHAR(32) NOT NULL CONSTRAINT [DF_DispatchShortages_Resolution] DEFAULT N'Pending',
    [TargetDispatchId] UNIQUEIDENTIFIER NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ResolvedBy] UNIQUEIDENTIFIER NULL,
    [ResolvedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_DispatchShortages] PRIMARY KEY ([DispatchShortageId]),
    CONSTRAINT [FK_DispatchShortages_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchShortages_Lines] FOREIGN KEY ([DispatchLineId]) REFERENCES [dbo].[DispatchLines] ([DispatchLineId]),
    CONSTRAINT [FK_DispatchShortages_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_DispatchShortages_Target] FOREIGN KEY ([TargetDispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [CK_DispatchShortages_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_DispatchShortages_Resolution] CHECK ([ResolutionStatus] IN (N'Pending',N'ReassignedToAnotherDispatch',N'CancelledByCommercialCorrection',N'ResolvedWithoutDispatch'))
);
GO

CREATE TABLE [dbo].[DispatchVerificationEvents] (
    [DispatchVerificationEventId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchLineId] UNIQUEIDENTIFIER NULL,
    [ProductId] UNIQUEIDENTIFIER NULL,
    [Barcode] NVARCHAR(64) NULL,
    [QuantityDelta] DECIMAL(19,6) NOT NULL,
    [EventType] NVARCHAR(32) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    CONSTRAINT [PK_DispatchVerificationEvents] PRIMARY KEY ([DispatchVerificationEventId]),
    CONSTRAINT [FK_DispatchVerificationEvents_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchVerificationEvents_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchVerificationEvents_Lines] FOREIGN KEY ([DispatchLineId]) REFERENCES [dbo].[DispatchLines] ([DispatchLineId]),
    CONSTRAINT [FK_DispatchVerificationEvents_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_DispatchVerificationEvents_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchVerificationEvents_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_DispatchVerificationEvents_Type] CHECK ([EventType] IN (N'Scanned',N'QuantityEdited',N'ScanUndone',N'ShortageDeclared',N'StatusChanged',N'SupervisorOverride'))
);
GO
CREATE INDEX [IX_DispatchVerificationEvents_Dispatch_Time] ON [dbo].[DispatchVerificationEvents] ([DispatchId],[OccurredAt]);
GO
