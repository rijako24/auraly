CREATE TABLE [dbo].[GoodsReceipts]
(
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL,
    [PurchaseOrderId] UNIQUEIDENTIFIER NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [PurchaseEvidenceType] NVARCHAR(40) NOT NULL,
    [SupportFiscalSeriesId] UNIQUEIDENTIFIER NULL,
    [SupportFiscalAuthorizationId] UNIQUEIDENTIFIER NULL,
    [SupportFiscalNumber] NVARCHAR(64) NULL,
    [SupplierInvoiceNumber] NVARCHAR(80) NULL,
    [SupplierInvoiceDate] DATETIMEOFFSET(7) NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatesPayable] BIT NOT NULL,
    [DueDate] DATETIMEOFFSET(7) NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [GrandTotal] DECIMAL(19,4) NOT NULL,
    [ExchangeRate] DECIMAL(19,8) NOT NULL CONSTRAINT [DF_GoodsReceipts_ExchangeRate] DEFAULT 1,
    [ExchangeRateDate] DATE NOT NULL CONSTRAINT [DF_GoodsReceipts_ExchangeRateDate] DEFAULT CONVERT(date,'20000101'),
    [ExchangeRateSource] NVARCHAR(64) NOT NULL CONSTRAINT [DF_GoodsReceipts_ExchangeRateSource] DEFAULT N'FunctionalCurrency',
    [FunctionalNetAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceipts_FunctionalNet] DEFAULT 0,
    [FunctionalTaxAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceipts_FunctionalTax] DEFAULT 0,
    [FunctionalGrandTotal] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceipts_FunctionalTotal] DEFAULT 0,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_GoodsReceipts] PRIMARY KEY CLUSTERED ([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_GoodsReceipts_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_GoodsReceipts_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_GoodsReceipts_PartySite] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [FK_GoodsReceipts_PurchaseOrder] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [purchasing].[PurchaseOrders] ([PurchaseOrderId]),
    CONSTRAINT [FK_GoodsReceipts_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_GoodsReceipts_SupportFiscalSeries] FOREIGN KEY ([SupportFiscalSeriesId]) REFERENCES [dbo].[FiscalSeries] ([SeriesId]),
    CONSTRAINT [FK_GoodsReceipts_SupportFiscalAuthorization] FOREIGN KEY ([SupportFiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [UQ_GoodsReceipts_Business_Number] UNIQUE ([BusinessId], [DocumentNumber]),
    CONSTRAINT [UQ_GoodsReceipts_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_GoodsReceipts_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrandTotal] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_GoodsReceipts_Payable] CHECK (([CreatesPayable] = 0) OR ([DueDate] IS NOT NULL)),
    CONSTRAINT [CK_GoodsReceipts_Status] CHECK ([Status] IN (N'Accepted', N'Processed'))
    ,CONSTRAINT [CK_GoodsReceipts_PurchaseEvidenceType] CHECK ([PurchaseEvidenceType] IN
      (N'SupplierElectronicInvoice',N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher',N'ForeignCommercialInvoice'))
    ,CONSTRAINT [CK_GoodsReceipts_SupportFiscalData] CHECK (
      ([PurchaseEvidenceType]=N'BuyerElectronicSupportDocument' AND [SupportFiscalSeriesId] IS NOT NULL AND [SupportFiscalAuthorizationId] IS NOT NULL AND [SupportFiscalNumber] IS NOT NULL)
      OR ([PurchaseEvidenceType]<>N'BuyerElectronicSupportDocument' AND [SupportFiscalSeriesId] IS NULL AND [SupportFiscalAuthorizationId] IS NULL AND [SupportFiscalNumber] IS NULL))
);
GO
CREATE INDEX [IX_GoodsReceipts_Business_Received]
    ON [dbo].[GoodsReceipts] ([BusinessId], [ReceivedAt] DESC)
    INCLUDE ([SupplierId], [WarehouseId], [Status], [GrandTotal]);
GO
CREATE INDEX [IX_GoodsReceipts_PurchaseOrder_Status]
    ON [dbo].[GoodsReceipts] ([PurchaseOrderId], [Status])
    INCLUDE ([GoodsReceiptId])
    WHERE [PurchaseOrderId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_GoodsReceipts_Supplier_Invoice]
    ON [dbo].[GoodsReceipts] ([BusinessId], [SupplierId], [SupplierInvoiceNumber])
    WHERE [SupplierInvoiceNumber] IS NOT NULL;
GO

CREATE TABLE [dbo].[GoodsReceiptLines]
(
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [PurchaseOrderLineId] UNIQUEIDENTIFIER NULL,
    [OverReceiptReason] NVARCHAR(500) NULL,
    [OverReceiptAuthorized] BIT NOT NULL CONSTRAINT [DF_GoodsReceiptLines_OverReceiptAuthorized] DEFAULT 0,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [PresentationNameSnapshot] NVARCHAR(80) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_PresentationName] DEFAULT N'Unidad',
    [PresentationQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_PresentationQuantity] DEFAULT 1,
    [UnitsPerPresentation] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_UnitsPerPresentation] DEFAULT 1,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL
        CONSTRAINT [DF_GoodsReceiptLines_TaxTreatment] DEFAULT (N'DeductibleInputVat'),
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    [TotalGrossWeightKg] DECIMAL(19,6) NULL,
    [TotalVolumeM3] DECIMAL(19,6) NULL,
    [FunctionalNetAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_FunctionalNet] DEFAULT 0,
    [FunctionalTaxAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_FunctionalTax] DEFAULT 0,
    [FunctionalLineTotal] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_FunctionalTotal] DEFAULT 0,
    [AllocatedLandedCostAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_LandedCost] DEFAULT 0,
    [RecognizedInventoryCostAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_GoodsReceiptLines_RecognizedCost] DEFAULT 0,
    [CommercialDiscountPercent] DECIMAL(9,6) NULL,
    CONSTRAINT [PK_GoodsReceiptLines] PRIMARY KEY CLUSTERED ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [FK_GoodsReceiptLines_Receipts] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts] ([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceiptLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_GoodsReceiptLines_PurchaseOrderLine] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [purchasing].[PurchaseOrderLines] ([LineId]),
    CONSTRAINT [CK_GoodsReceiptLines_OverReceipt] CHECK (([OverReceiptAuthorized]=0 AND [OverReceiptReason] IS NULL) OR ([OverReceiptAuthorized]=1 AND [PurchaseOrderLineId] IS NOT NULL AND [OverReceiptReason] IS NOT NULL)),
    CONSTRAINT [CK_GoodsReceiptLines_Amounts] CHECK ([LineNumber] > 0 AND [Quantity] > 0 AND [PresentationQuantity] > 0 AND [UnitsPerPresentation] > 0 AND [Quantity] = [PresentationQuantity] * [UnitsPerPresentation] AND [UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxRate] BETWEEN 0 AND 100 AND [LineTotal] = [NetAmount] + [TaxAmount] AND ([TotalGrossWeightKg] IS NULL OR [TotalGrossWeightKg]>0) AND ([TotalVolumeM3] IS NULL OR [TotalVolumeM3]>0) AND [AllocatedLandedCostAmount]>=0 AND [RecognizedInventoryCostAmount]>=0),
    CONSTRAINT [CK_GoodsReceiptLines_TaxTreatment] CHECK ([TaxTreatment] IN (N'DeductibleInputVat', N'CapitalizedCost', N'NotApplicable')),
    CONSTRAINT [CK_GoodsReceiptLines_CommercialDiscount] CHECK ([CommercialDiscountPercent] IS NULL OR [CommercialDiscountPercent] BETWEEN 0 AND 100)
);
GO
CREATE INDEX [IX_GoodsReceiptLines_Product] ON [dbo].[GoodsReceiptLines] ([ProductId], [GoodsReceiptId]);
GO
CREATE INDEX [IX_GoodsReceiptLines_PurchaseOrderLine]
    ON [dbo].[GoodsReceiptLines] ([PurchaseOrderLineId], [GoodsReceiptId])
    INCLUDE ([Quantity])
    WHERE [PurchaseOrderLineId] IS NOT NULL;
GO

-- Each post-confirmation cost change has its own business identity. The
-- document engine remains the only job owner; these rows describe revisions.
CREATE TABLE [purchasing].[GoodsReceiptCostRevisions]
(
    [RevisionId] UNIQUEIDENTIFIER NOT NULL,
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RevisionNumber] INT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [AcceptedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_GoodsReceiptCostRevisions] PRIMARY KEY CLUSTERED ([RevisionId]),
    CONSTRAINT [FK_GoodsReceiptCostRevisions_Receipt] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts]([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceiptCostRevisions_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_GoodsReceiptCostRevisions_User] FOREIGN KEY ([AcceptedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_GoodsReceiptCostRevisions_Number] UNIQUE ([GoodsReceiptId],[RevisionNumber]),
    CONSTRAINT [UQ_GoodsReceiptCostRevisions_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_GoodsReceiptCostRevisions_Number] CHECK ([RevisionNumber]>0),
    CONSTRAINT [CK_GoodsReceiptCostRevisions_Status] CHECK ([Status] IN(N'Accepted',N'Processed'))
);
GO

CREATE TABLE [purchasing].[GoodsReceiptCostDocuments]
(
    [CostDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL,
    [PurchaseEvidenceType] NVARCHAR(40) NULL,
    [DocumentNumber] NVARCHAR(80) NULL,
    [IssuedAt] DATETIMEOFFSET(7) NULL,
    [CreatesPayable] BIT NULL,
    [DueDate] DATETIMEOFFSET(7) NULL,
    [CurrencyCode] CHAR(3) NULL,
    [ExchangeRate] DECIMAL(19,8) NULL,
    [ExchangeRateDate] DATE NULL,
    [ExchangeRateSource] NVARCHAR(64) NULL,
    [NetAmount] DECIMAL(19,4) NULL,
    [TaxAmount] DECIMAL(19,4) NULL,
    [GrandTotal] DECIMAL(19,4) NULL,
    [FunctionalNetAmount] DECIMAL(19,4) NULL,
    [FunctionalTaxAmount] DECIMAL(19,4) NULL,
    [FunctionalGrandTotal] DECIMAL(19,4) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [SourceEntryId] UNIQUEIDENTIFIER NULL,
    [AssociationMode] NVARCHAR(16) NULL,
    [CostRevisionId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_GoodsReceiptCostDocuments] PRIMARY KEY ([CostDocumentId]),
    CONSTRAINT [FK_GoodsReceiptCostDocuments_Receipt] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts]([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceiptCostDocuments_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers]([SupplierId]),
    CONSTRAINT [FK_GoodsReceiptCostDocuments_PartySite] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites]([PartySiteId]),
    CONSTRAINT [UQ_GoodsReceiptCostDocuments_SourceBinding] UNIQUE ([CostDocumentId],[SourceEntryId]),
    CONSTRAINT [FK_GoodsReceiptCostDocuments_SourceEntry] FOREIGN KEY ([SourceEntryId]) REFERENCES [dbo].[AccountingEntries]([EntryId]),
    CONSTRAINT [FK_GoodsReceiptCostDocuments_Revision] FOREIGN KEY ([CostRevisionId]) REFERENCES [purchasing].[GoodsReceiptCostRevisions]([RevisionId]),
    CONSTRAINT [CK_GoodsReceiptCostDocuments_SourceShape] CHECK (
      ([SourceEntryId] IS NULL AND [AssociationMode] IS NULL AND [SupplierId] IS NOT NULL
        AND [PurchaseEvidenceType] IS NOT NULL AND [DocumentNumber] IS NOT NULL AND [IssuedAt] IS NOT NULL
        AND [CreatesPayable] IS NOT NULL AND [CurrencyCode] IS NOT NULL AND [ExchangeRate] IS NOT NULL
        AND [ExchangeRateDate] IS NOT NULL AND [ExchangeRateSource] IS NOT NULL AND [NetAmount] IS NOT NULL
        AND [TaxAmount] IS NOT NULL AND [GrandTotal] IS NOT NULL AND [FunctionalNetAmount] IS NOT NULL
        AND [FunctionalTaxAmount] IS NOT NULL AND [FunctionalGrandTotal] IS NOT NULL)
      OR ([SourceEntryId] IS NOT NULL AND [AssociationMode] IS NOT NULL
        AND [AssociationMode] IN(N'Associate',N'Distribute') AND [SupplierId] IS NULL AND [PartySiteId] IS NULL
        AND [PurchaseEvidenceType] IS NULL AND [DocumentNumber] IS NULL AND [IssuedAt] IS NULL
        AND [CreatesPayable] IS NULL AND [DueDate] IS NULL AND [CurrencyCode] IS NULL AND [ExchangeRate] IS NULL
        AND [ExchangeRateDate] IS NULL AND [ExchangeRateSource] IS NULL AND [NetAmount] IS NULL
        AND [TaxAmount] IS NULL AND [GrandTotal] IS NULL AND [FunctionalNetAmount] IS NULL
        AND [FunctionalTaxAmount] IS NULL AND [FunctionalGrandTotal] IS NULL)),
    CONSTRAINT [CK_GoodsReceiptCostDocuments_Evidence] CHECK ([PurchaseEvidenceType] IN (N'SupplierElectronicInvoice',N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher',N'ForeignCommercialInvoice',N'ImportDeclaration')),
    CONSTRAINT [CK_GoodsReceiptCostDocuments_Amounts] CHECK ([ExchangeRate]>0 AND [NetAmount]>=0 AND [TaxAmount]>=0 AND [GrandTotal]=[NetAmount]+[TaxAmount] AND [FunctionalNetAmount]>=0 AND [FunctionalTaxAmount]>=0 AND [FunctionalGrandTotal]=[FunctionalNetAmount]+[FunctionalTaxAmount]),
    CONSTRAINT [CK_GoodsReceiptCostDocuments_Payable] CHECK ([CreatesPayable]=0 OR [DueDate] IS NOT NULL)
);
GO
CREATE INDEX [IX_GoodsReceiptCostDocuments_Receipt] ON [purchasing].[GoodsReceiptCostDocuments]([GoodsReceiptId],[IssuedAt]);
GO

CREATE UNIQUE INDEX [UX_GoodsReceiptCostDocuments_CapturedNumber]
    ON [purchasing].[GoodsReceiptCostDocuments]([SupplierId],[DocumentNumber]) WHERE [SourceEntryId] IS NULL;
GO
CREATE UNIQUE INDEX [UX_GoodsReceiptCostDocuments_ReferencedEntry]
    ON [purchasing].[GoodsReceiptCostDocuments]([GoodsReceiptId],[SourceEntryId],[CostRevisionId]) WHERE [SourceEntryId] IS NOT NULL;
GO
CREATE INDEX [IX_GoodsReceiptCostDocuments_SourceEntry] ON [purchasing].[GoodsReceiptCostDocuments]([SourceEntryId])
    WHERE [SourceEntryId] IS NOT NULL;
GO
CREATE TABLE [purchasing].[GoodsReceiptCostLines]
(
    [CostDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [CostKind] NVARCHAR(32) NULL,
    [DescriptionSnapshot] NVARCHAR(250) NULL,
    [Amount] DECIMAL(19,4) NULL,
    [TaxableBaseAmount] DECIMAL(19,4) NULL,
    [TaxCode] NVARCHAR(32) NULL,
    [TaxRate] DECIMAL(9,6) NULL,
    [TaxAmount] DECIMAL(19,4) NULL,
    [TaxTreatment] NVARCHAR(32) NULL,
    [CostTreatment] NVARCHAR(16) NULL,
    [AllocationMethod] NVARCHAR(16) NOT NULL,
    [FunctionalAmount] DECIMAL(19,4) NOT NULL,
    [FunctionalTaxableBaseAmount] DECIMAL(19,4) NULL,
    [FunctionalTaxAmount] DECIMAL(19,4) NULL,
    [FunctionalDocumentAmount] DECIMAL(19,4) NULL,
    [SourceEntryId] UNIQUEIDENTIFIER NULL,
    [SourceEntryLineNumber] INT NULL,
    CONSTRAINT [FK_GoodsReceiptCostLines_SourceBinding] FOREIGN KEY ([CostDocumentId],[SourceEntryId])
      REFERENCES [purchasing].[GoodsReceiptCostDocuments]([CostDocumentId],[SourceEntryId]),
    CONSTRAINT [FK_GoodsReceiptCostLines_SourceLine] FOREIGN KEY ([SourceEntryId],[SourceEntryLineNumber])
      REFERENCES [dbo].[AccountingEntryLines]([EntryId],[LineNumber]),
    CONSTRAINT [CK_GoodsReceiptCostLines_SourceShape] CHECK (
      ([SourceEntryId] IS NULL AND [SourceEntryLineNumber] IS NULL
        AND [CostKind] IS NOT NULL AND [DescriptionSnapshot] IS NOT NULL AND [Amount] IS NOT NULL
        AND [TaxableBaseAmount] IS NOT NULL AND [TaxCode] IS NOT NULL AND [TaxRate] IS NOT NULL
        AND [TaxAmount] IS NOT NULL AND [TaxTreatment] IS NOT NULL AND [CostTreatment] IS NOT NULL
        AND [FunctionalTaxableBaseAmount] IS NOT NULL AND [FunctionalTaxAmount] IS NOT NULL AND [FunctionalDocumentAmount] IS NOT NULL)
      OR ([SourceEntryId] IS NOT NULL AND [SourceEntryLineNumber] IS NOT NULL AND [SourceEntryLineNumber]=[LineNumber]
        AND [FunctionalAmount]>0 AND [AllocationMethod]<>N'None'
        AND [CostKind] IS NULL AND [DescriptionSnapshot] IS NULL AND [Amount] IS NULL
        AND [TaxableBaseAmount] IS NULL AND [TaxCode] IS NULL AND [TaxRate] IS NULL
        AND [TaxAmount] IS NULL AND [TaxTreatment] IS NULL AND [CostTreatment] IS NULL
        AND [FunctionalTaxableBaseAmount] IS NULL AND [FunctionalTaxAmount] IS NULL AND [FunctionalDocumentAmount] IS NULL)),
    CONSTRAINT [PK_GoodsReceiptCostLines] PRIMARY KEY ([CostDocumentId],[LineNumber]),
    CONSTRAINT [FK_GoodsReceiptCostLines_Document] FOREIGN KEY ([CostDocumentId]) REFERENCES [purchasing].[GoodsReceiptCostDocuments]([CostDocumentId]),
    CONSTRAINT [CK_GoodsReceiptCostLines_Kind] CHECK ([CostKind] IN (N'Freight',N'Insurance',N'CustomsDuty',N'CustomsBrokerage',N'Handling',N'OtherDirectCost',N'ImportVat')),
    CONSTRAINT [CK_GoodsReceiptCostLines_Treatment] CHECK ([TaxTreatment] IN (N'DeductibleInputVat',N'CapitalizedCost',N'NotApplicable') AND [CostTreatment] IN (N'Capitalize',N'Expense')),
    CONSTRAINT [CK_GoodsReceiptCostLines_Allocation] CHECK ([AllocationMethod] IN (N'Value',N'Quantity',N'Weight',N'Volume',N'Equal',N'Manual',N'None') AND ([CostTreatment]=N'Capitalize' OR [AllocationMethod]=N'None')),
    CONSTRAINT [CK_GoodsReceiptCostLines_Amounts] CHECK ([LineNumber]>0 AND [Amount]>=0 AND [TaxableBaseAmount]>=0 AND [TaxRate] BETWEEN 0 AND 100 AND [TaxAmount]>=0 AND [FunctionalAmount]>=0 AND [FunctionalTaxAmount]>=0 AND [FunctionalDocumentAmount]=[FunctionalAmount]+[FunctionalTaxAmount])
);
GO

CREATE INDEX [IX_GoodsReceiptCostLines_SourceBudget]
    ON [purchasing].[GoodsReceiptCostLines]([SourceEntryId],[SourceEntryLineNumber]) INCLUDE([FunctionalAmount]) WHERE [SourceEntryId] IS NOT NULL;
GO
CREATE TABLE [purchasing].[GoodsReceiptCostAllocations]
(
    [CostDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [CostLineNumber] INT NOT NULL,
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [ReceiptLineNumber] INT NOT NULL,
    [AllocationMethod] NVARCHAR(16) NOT NULL,
    [AllocationFactor] DECIMAL(19,12) NOT NULL,
    [FunctionalAmount] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_GoodsReceiptCostAllocations] PRIMARY KEY ([CostDocumentId],[CostLineNumber],[ReceiptLineNumber]),
    CONSTRAINT [FK_GoodsReceiptCostAllocations_CostLine] FOREIGN KEY ([CostDocumentId],[CostLineNumber]) REFERENCES [purchasing].[GoodsReceiptCostLines]([CostDocumentId],[LineNumber]),
    CONSTRAINT [FK_GoodsReceiptCostAllocations_ReceiptLine] FOREIGN KEY ([GoodsReceiptId],[ReceiptLineNumber]) REFERENCES [dbo].[GoodsReceiptLines]([GoodsReceiptId],[LineNumber]),
    CONSTRAINT [CK_GoodsReceiptCostAllocations_Value] CHECK ([AllocationFactor]>=0 AND [FunctionalAmount]>=0)
);
GO

CREATE TABLE [dbo].[SupplierCostObservations]
(
    [SupplierCostObservationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ObservedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SupplierCostObservations] PRIMARY KEY CLUSTERED ([SupplierCostObservationId]),
    CONSTRAINT [FK_SupplierCostObservations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierCostObservations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierCostObservations_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_SupplierCostObservations_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [UQ_SupplierCostObservations_Source] UNIQUE ([SourceDocumentId], [SourceLineNumber]),
    CONSTRAINT [CK_SupplierCostObservations_Cost] CHECK ([UnitCost] >= 0)
);
GO
CREATE INDEX [IX_SupplierCostObservations_Product]
    ON [dbo].[SupplierCostObservations] ([BusinessId], [SupplierId], [ProductId], [ObservedAt] DESC);
GO

CREATE TABLE [dbo].[SupplierProductLatestCosts]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [PreviousUnitCost] DECIMAL(19,6) NULL,
    [LatestUnitCost] DECIMAL(19,6) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [ObservedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SupplierProductLatestCosts] PRIMARY KEY CLUSTERED ([BusinessId], [SupplierId], [ProductId]),
    CONSTRAINT [FK_SupplierProductLatestCosts_Association] FOREIGN KEY ([ProductId], [SupplierId]) REFERENCES [dbo].[SupplierProducts] ([ProductId], [SupplierId]),
    CONSTRAINT [FK_SupplierProductLatestCosts_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [CK_SupplierProductLatestCosts_Cost] CHECK ([LatestUnitCost] >= 0 AND ([PreviousUnitCost] IS NULL OR [PreviousUnitCost] >= 0))
);
GO

CREATE TABLE [dbo].[Payables]
(
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [DocumentNumber] NVARCHAR(80) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [OutstandingAmount] DECIMAL(19,4) NOT NULL,
    [FunctionalOriginalAmount] DECIMAL(19,4) NULL,
    -- Rolling-release compatibility only; no current runtime reader or writer.
    -- New positions leave this NULL. Retire after legacy writers and their rollback window close.
    [FunctionalOutstandingAmount] DECIMAL(19,4) NULL,
    [ExchangeRate] DECIMAL(19,8) NOT NULL CONSTRAINT [DF_Payables_ExchangeRate] DEFAULT 1,
    [ParentGoodsReceiptId] UNIQUEIDENTIFIER NULL,
    [DueDate] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [OpeningEntryId] UNIQUEIDENTIFIER NULL,
    [OpeningEntryLineNumber] INT NULL,
    -- Legacy/native sources retain their single NULL slot; manual captures
    -- identify each obligation by its immutable source line.
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Payables] PRIMARY KEY CLUSTERED ([PayableId]),
    CONSTRAINT [FK_Payables_OpeningEntryLine] FOREIGN KEY ([OpeningEntryId],[OpeningEntryLineNumber]) REFERENCES [dbo].[AccountingEntryLines] ([EntryId],[LineNumber]),
    CONSTRAINT [CK_Payables_OpeningEntryLine] CHECK (([OpeningEntryId] IS NULL AND [OpeningEntryLineNumber] IS NULL) OR ([OpeningEntryId] IS NOT NULL AND [OpeningEntryLineNumber] IS NOT NULL)),
    CONSTRAINT [FK_Payables_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Payables_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_Payables_PartySites] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [FK_Payables_SourceJob] FOREIGN KEY ([SourceDocumentId], [SourceDocumentType]) REFERENCES [dbo].[AccountingPostingJobs] ([SourceDocumentId], [SourceDocumentType]),
    CONSTRAINT [FK_Payables_ParentGoodsReceipt] FOREIGN KEY ([ParentGoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts]([GoodsReceiptId]),
    CONSTRAINT [UQ_Payables_Source] UNIQUE ([SourceDocumentId], [SourceDocumentType], [SourceLineId]),
    CONSTRAINT [CK_Payables_SourceLine] CHECK ([SourceLineId] IS NULL OR
      ([SourceLineId]<>'00000000-0000-0000-0000-000000000000' AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [CK_Payables_Amounts] CHECK ([OriginalAmount] > 0 AND [OutstandingAmount] >= 0 AND ([FunctionalOriginalAmount] IS NULL OR [FunctionalOriginalAmount]>0) AND [ExchangeRate]>0),
    CONSTRAINT [CK_Payables_Status] CHECK ([Status] IN (N'Open', N'PartiallyPaid', N'Paid', N'Cancelled'))
);
GO
CREATE INDEX [IX_Payables_Business_Due] ON [dbo].[Payables] ([BusinessId], [Status], [DueDate]);
GO
CREATE INDEX [IX_Payables_Business_Supplier_Site] ON [dbo].[Payables] ([BusinessId], [SupplierId], [PartySiteId], [Status], [DueDate]);
GO
CREATE INDEX [IX_Payables_OpenPositionPage] ON [dbo].[Payables] ([BusinessId], [DueDate], [PayableId])
    INCLUDE ([SupplierId], [SourceDocumentId], [SourceDocumentType], [DocumentNumber],
        [OriginalAmount], [OutstandingAmount], [OpeningEntryId], [OpeningEntryLineNumber], [PartySiteId], [Status])
    WHERE [CurrencyCode] = 'COP' AND [OutstandingAmount] > 0 AND [Status] IN (N'Open', N'PartiallyPaid');
GO

CREATE TABLE [dbo].[PayableTransactions]
(
    [PayableTransactionId] UNIQUEIDENTIFIER NOT NULL,
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [TransactionType] NVARCHAR(24) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NULL,
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_PayableTransactions] PRIMARY KEY CLUSTERED ([PayableTransactionId]),
    [ReversesTransactionId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [FK_PayableTransactions_Reversal] FOREIGN KEY ([ReversesTransactionId])
      REFERENCES [dbo].[PayableTransactions]([PayableTransactionId]),
    CONSTRAINT [CK_PayableTransactions_Reversal] CHECK (
      ([TransactionType] NOT IN(N'Unapplication',N'CreditUnapplication') AND [ReversesTransactionId] IS NULL) OR
      ([TransactionType] IN(N'Unapplication',N'CreditUnapplication') AND [ReversesTransactionId] IS NOT NULL AND [Amount]>0 AND [SourceDocumentType]=N'ManualAccountingVoucher' AND [SourceLineId] IS NOT NULL)),
    CONSTRAINT [FK_PayableTransactions_Payables] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [FK_PayableTransactions_TypedSource] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType])
      REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [UQ_PayableTransactions_Payable_Source_Type] UNIQUE ([PayableId], [SourceDocumentId], [TransactionType],[SourceLineId]),
    CONSTRAINT [CK_PayableTransactions_Line] CHECK ([SourceLineId] IS NULL OR
      ([SourceLineId]<>'00000000-0000-0000-0000-000000000000' AND [SourceDocumentType] IS NOT NULL AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [CK_PayableTransactions_Amount] CHECK ([Amount] <> 0),
    CONSTRAINT [CK_PayableTransactions_Type] CHECK ([TransactionType] IN (N'Opening', N'Payment', N'Credit', N'Adjustment', N'CreditApplication',N'Unapplication',N'CreditUnapplication'))
);
GO
CREATE INDEX [IX_PayableTransactions_Unapplications] ON [dbo].[PayableTransactions]([PayableId],[OccurredAt])
    INCLUDE([Amount],[ReversesTransactionId],[CreatedAt]) WHERE [TransactionType]=N'Unapplication';
GO

CREATE TABLE [dbo].[PriceRevisionProposals]
(
    [PriceRevisionProposalId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [SourceRevision] INT NOT NULL CONSTRAINT [DF_PriceRevisionProposals_SourceRevision] DEFAULT 0,
    [PreviousObservedUnitCost] DECIMAL(19,6) NULL,
    [ObservedUnitCost] DECIMAL(19,6) NOT NULL,
    [CurrentSalePrice] DECIMAL(19,4) NOT NULL,
    [CurrentMarginPercent] DECIMAL(9,6) NULL,
    [TargetMarginPercent] DECIMAL(9,6) NULL,
    [SuggestedSalePrice] DECIMAL(19,4) NOT NULL,
    [RoundedSuggestedSalePrice] DECIMAL(19,4) NULL,
    [EffectiveMarginAfterRounding] DECIMAL(9,6) NULL,
    [RejectReason] NVARCHAR(500) NULL,
    [LastInputMode] NVARCHAR(16) NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ReviewedByUserId] UNIQUEIDENTIFIER NULL,
    [ReviewedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PriceRevisionProposals] PRIMARY KEY CLUSTERED ([PriceRevisionProposalId]),
    CONSTRAINT [FK_PriceRevisionProposals_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PriceRevisionProposals_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_PriceRevisionProposals_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [UQ_PriceRevisionProposals_Source] UNIQUE ([BusinessId], [SourceDocumentId], [SourceLineNumber], [SourceRevision]),
    CONSTRAINT [CK_PriceRevisionProposals_SourceRevision] CHECK ([SourceRevision]>=0),
    CONSTRAINT [CK_PriceRevisionProposals_Values] CHECK ([ObservedUnitCost] >= 0 AND [CurrentSalePrice] >= 0 AND [SuggestedSalePrice] >= 0 AND ([CurrentMarginPercent] IS NULL OR [CurrentMarginPercent] < 100) AND ([TargetMarginPercent] IS NULL OR [TargetMarginPercent] BETWEEN 0 AND 99.999999)),
    CONSTRAINT [CK_PriceRevisionProposals_Rounded] CHECK ([RoundedSuggestedSalePrice] IS NULL OR [RoundedSuggestedSalePrice] >= 0),
    CONSTRAINT [CK_PriceRevisionProposals_InputMode] CHECK ([LastInputMode] IS NULL OR [LastInputMode] IN (N'Margin',N'SalePrice')),
    CONSTRAINT [CK_PriceRevisionProposals_Status] CHECK ([Status] IN (N'PendingReview', N'Approved', N'Published', N'Rejected', N'Superseded'))
);
GO
CREATE INDEX [IX_PriceRevisionProposals_Business_Status]
    ON [dbo].[PriceRevisionProposals] ([BusinessId], [Status], [CreatedAt] DESC)
    INCLUDE ([ProductId], [ObservedUnitCost], [CurrentSalePrice], [SuggestedSalePrice]);
GO
