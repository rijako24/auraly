CREATE TABLE [reporting].[SalesReportDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [FiscalNumber] NVARCHAR(64) NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [TimeZoneId] NVARCHAR(100) NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseName] NVARCHAR(200) NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [SellerId] UNIQUEIDENTIFIER NULL,
    [SellerName] NVARCHAR(201) NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [CustomerIdentification] NVARCHAR(64) NOT NULL,
    [CustomerName] NVARCHAR(240) NOT NULL,
    [SourceMode] NVARCHAR(16) NOT NULL,
    [FiscalStatus] NVARCHAR(40) NULL,
    [CurrencyCode] CHAR(3) NOT NULL CONSTRAINT [DF_SalesReportDocuments_Currency] DEFAULT N'COP',
    [GrossAmount] DECIMAL(19,4) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [UntaxedAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [CreditAmount] DECIMAL(19,4) NOT NULL,
    [CollectedAmount] DECIMAL(19,4) NOT NULL,
    [ReturnedUntaxedAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_SalesReportDocuments_ReturnedUntaxed] DEFAULT 0,
    [ReturnedTaxAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_SalesReportDocuments_ReturnedTax] DEFAULT 0,
    [ReturnedTotalAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_SalesReportDocuments_ReturnedTotal] DEFAULT 0,
    [RecognizedCostAmount] DECIMAL(19,4) NOT NULL,
    [ReturnedCostAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_SalesReportDocuments_ReturnedCost] DEFAULT 0,
    [ProjectionVersion] SMALLINT NOT NULL,
    [SourcePayloadHash] BINARY(32) NOT NULL,
    [ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReportDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesReportDocuments_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_SalesReportDocuments_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportDocuments_Type] CHECK ([DocumentType] IN (N'SalesInvoice',N'SalesReceipt')),
    CONSTRAINT [CK_SalesReportDocuments_Amounts] CHECK
      ([GrossAmount]>=0 AND [DiscountAmount]>=0 AND [UntaxedAmount]>=0 AND [TaxAmount]>=0 AND
       [TotalAmount]>=0 AND [CreditAmount]>=0 AND [CollectedAmount]>=0 AND
       [ReturnedUntaxedAmount]>=0 AND [ReturnedTaxAmount]>=0 AND [ReturnedTotalAmount]>=0 AND
       [RecognizedCostAmount]>=0 AND [ReturnedCostAmount]>=0),
    CONSTRAINT [CK_SalesReportDocuments_Projection] CHECK ([ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_SalesReportDocuments_Business_Date]
  ON [reporting].[SalesReportDocuments]([BusinessId],[BusinessLocalDate] DESC,[DocumentId] DESC)
  INCLUDE([DocumentNumber],[CustomerId],[SellerId],[WarehouseId],[WarehouseName],[SellerName],[UntaxedAmount],[TaxAmount],[TotalAmount],
          [ReturnedTotalAmount],[RecognizedCostAmount],[ReturnedCostAmount],[FiscalStatus]);
GO
CREATE INDEX [IX_SalesReportDocuments_Business_Customer_Date]
  ON [reporting].[SalesReportDocuments]([BusinessId],[CustomerId],[BusinessLocalDate] DESC,[DocumentId] DESC)
  WHERE [CustomerId] IS NOT NULL;
GO
CREATE INDEX [IX_SalesReportDocuments_Business_Seller_Date]
  ON [reporting].[SalesReportDocuments]([BusinessId],[SellerId],[BusinessLocalDate] DESC,[DocumentId] DESC)
  WHERE [SellerId] IS NOT NULL;
GO

CREATE TABLE [reporting].[SalesReportLineFacts]
(
    [FactId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(32) NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [OriginalSaleDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalLineNumber] INT NOT NULL,
    [MovementType] NVARCHAR(24) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [SellerId] UNIQUEIDENTIFIER NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCode] NVARCHAR(80) NOT NULL,
    [ProductName] NVARCHAR(300) NOT NULL,
    [CategoryId] UNIQUEIDENTIFIER NULL,
    [CategoryName] NVARCHAR(160) NULL,
    [SupplierId] UNIQUEIDENTIFIER NULL,
    [SupplierName] NVARCHAR(240) NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [GrossAmount] DECIMAL(19,4) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [UntaxedAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [RecognizedCostAmount] DECIMAL(19,4) NOT NULL,
    [ReturnReasonCode] NVARCHAR(40) NULL,
    [ReturnDisposition] NVARCHAR(24) NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReportLineFacts] PRIMARY KEY CLUSTERED ([FactId]),
    CONSTRAINT [UQ_SalesReportLineFacts_Source] UNIQUE
      ([SourceDocumentId],[SourceDocumentType],[SourceLineNumber]),
    CONSTRAINT [FK_SalesReportLineFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_SalesReportLineFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportLineFacts_Type] CHECK
      ([SourceDocumentType] IN (N'SalesInvoice',N'SalesReceipt',N'SalesReturn') AND
       [MovementType] IN (N'Sale',N'Return',N'DebitAdjustment',N'CreditAdjustment')),
    CONSTRAINT [CK_SalesReportLineFacts_Signed] CHECK
      (([MovementType] IN (N'Sale',N'DebitAdjustment') AND [Quantity]>0 AND [TotalAmount]>=0 AND [RecognizedCostAmount]>=0) OR
       ([MovementType] IN (N'Return',N'CreditAdjustment') AND [Quantity]<0 AND [TotalAmount]<=0 AND [RecognizedCostAmount]<=0)),
    CONSTRAINT [CK_SalesReportLineFacts_Projection] CHECK ([ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_SalesReportLineFacts_Business_Date]
  ON [reporting].[SalesReportLineFacts]([BusinessId],[BusinessLocalDate] DESC,[FactId] DESC)
  INCLUDE([ProductId],[SellerId],[CustomerId],[WarehouseId],[Quantity],[UntaxedAmount],[TaxAmount],
          [TotalAmount],[RecognizedCostAmount],[MovementType]);
GO
CREATE INDEX [IX_SalesReportLineFacts_Business_Product_Date]
  ON [reporting].[SalesReportLineFacts]([BusinessId],[ProductId],[BusinessLocalDate] DESC,[FactId] DESC)
  INCLUDE([Quantity],[UntaxedAmount],[TaxAmount],[TotalAmount],[RecognizedCostAmount],[MovementType]);
GO
CREATE INDEX [IX_SalesReportLineFacts_Business_Supplier_Date]
  ON [reporting].[SalesReportLineFacts]([BusinessId],[SupplierId],[BusinessLocalDate] DESC,[FactId] DESC)
  INCLUDE([ProductId],[CategoryId],[Quantity],[UntaxedAmount],[TaxAmount],[TotalAmount],[RecognizedCostAmount])
  WHERE [SupplierId] IS NOT NULL;
GO

CREATE TABLE [reporting].[SalesReportDailyDimensionTotals]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [DimensionType] NVARCHAR(24) NOT NULL,
    [DimensionKey] NVARCHAR(80) NOT NULL,
    [DimensionLabel] NVARCHAR(300) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [DocumentCount] BIGINT NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [GrossSales] DECIMAL(19,4) NOT NULL,
    [Discounts] DECIMAL(19,4) NOT NULL,
    [Returns] DECIMAL(19,4) NOT NULL,
    [NetUntaxedSales] DECIMAL(19,4) NOT NULL,
    [NetTax] DECIMAL(19,4) NOT NULL,
    [NetTotalSales] DECIMAL(19,4) NOT NULL,
    [NetRecognizedCost] DECIMAL(19,4) NOT NULL,
    [GrossProfit] DECIMAL(19,4) NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReportDailyDimensionTotals] PRIMARY KEY CLUSTERED
      ([BusinessId],[BusinessLocalDate],[DimensionType],[DimensionKey],[CurrencyCode]),
    CONSTRAINT [FK_SalesReportDailyDimensionTotals_Business]
      FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportDailyDimensionTotals_Type] CHECK
      ([DimensionType] IN (N'Customer',N'Seller',N'Supplier',N'Product',N'Category',N'Warehouse')),
    CONSTRAINT [CK_SalesReportDailyDimensionTotals_Version] CHECK ([ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_SalesReportDailyDimensionTotals_Query]
  ON [reporting].[SalesReportDailyDimensionTotals]([BusinessId],[DimensionType],[BusinessLocalDate],[DimensionKey])
  INCLUDE([DimensionLabel],[DocumentCount],[Quantity],[GrossSales],[Discounts],[Returns],
          [NetUntaxedSales],[NetTax],[NetTotalSales],[NetRecognizedCost],[GrossProfit]);
GO

CREATE TABLE [reporting].[SalesReportPaymentFacts]
(
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(32) NOT NULL,
    [PaymentNumber] INT NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [MovementType] NVARCHAR(24) NOT NULL,
    [MethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReportPaymentFacts] PRIMARY KEY CLUSTERED
      ([SourceDocumentId],[SourceDocumentType],[PaymentNumber]),
    CONSTRAINT [FK_SalesReportPaymentFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_SalesReportPaymentFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportPaymentFacts_Type] CHECK ([MovementType] IN (N'Payment',N'Credit',N'Refund',N'CreditApplication')),
    CONSTRAINT [CK_SalesReportPaymentFacts_Amount] CHECK ([Amount]<>0)
);
GO
CREATE INDEX [IX_SalesReportPaymentFacts_Business_Date_Method]
  ON [reporting].[SalesReportPaymentFacts]([BusinessId],[BusinessLocalDate],[MethodCode]) INCLUDE([Amount],[MovementType]);
GO

CREATE TABLE [reporting].[SalesReportTaxFacts]
(
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(32) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [TaxableAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReportTaxFacts] PRIMARY KEY CLUSTERED
      ([SourceDocumentId],[SourceDocumentType],[TaxCode],[TaxRate]),
    CONSTRAINT [FK_SalesReportTaxFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_SalesReportTaxFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId])
);
GO
CREATE INDEX [IX_SalesReportTaxFacts_Business_Date_Tax]
  ON [reporting].[SalesReportTaxFacts]([BusinessId],[BusinessLocalDate],[TaxCode],[TaxRate])
  INCLUDE([TaxableAmount],[TaxAmount],[TotalAmount]);
GO

CREATE TABLE [reporting].[SalesReportDailyTotals]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [DocumentCount] BIGINT NOT NULL,
    [UnitsSold] DECIMAL(19,6) NOT NULL,
    [UnitsReturned] DECIMAL(19,6) NOT NULL,
    [GrossSales] DECIMAL(19,4) NOT NULL,
    [Discounts] DECIMAL(19,4) NOT NULL,
    [Returns] DECIMAL(19,4) NOT NULL,
    [NetUntaxedSales] DECIMAL(19,4) NOT NULL,
    [NetTax] DECIMAL(19,4) NOT NULL,
    [NetTotalSales] DECIMAL(19,4) NOT NULL,
    [NetRecognizedCost] DECIMAL(19,4) NOT NULL,
    [GrossProfit] DECIMAL(19,4) NOT NULL,
    [CreditSales] DECIMAL(19,4) NOT NULL,
    [Collected] DECIMAL(19,4) NOT NULL,
    [Refunded] DECIMAL(19,4) NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReportDailyTotals] PRIMARY KEY CLUSTERED
      ([BusinessId],[BusinessLocalDate],[CurrencyCode]),
    CONSTRAINT [FK_SalesReportDailyTotals_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportDailyTotals_Counts] CHECK ([DocumentCount]>=0 AND [UnitsSold]>=0 AND [UnitsReturned]>=0),
    CONSTRAINT [CK_SalesReportDailyTotals_Projection] CHECK ([ProjectionVersion]>0)
);
GO

CREATE TABLE [reporting].[SalesReportingCheckpoints]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [LastSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [LastSourceDocumentType] NVARCHAR(32) NOT NULL,
    [LastProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastError] NVARCHAR(1000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReportingCheckpoints] PRIMARY KEY CLUSTERED ([BusinessId],[ProjectionVersion]),
    CONSTRAINT [FK_SalesReportingCheckpoints_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_SalesReportingCheckpoints_Version] CHECK ([ProjectionVersion]>0)
);
GO

CREATE TABLE [reporting].[SalesReportingJobs]
(
    [SalesReportingJobId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [SourceVersion] BIGINT NOT NULL CONSTRAINT [DF_SalesReportingJobs_SourceVersion] DEFAULT (1),
    [SourcePayloadHash] BINARY(32) NOT NULL,
    [SourcePayloadJson] NVARCHAR(MAX) NULL,
    [SourceDocumentProcessingJobId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_SalesReportingJobs_Attempts] DEFAULT 0,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(2000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReportingJobs] PRIMARY KEY CLUSTERED ([SalesReportingJobId]),
    CONSTRAINT [UQ_SalesReportingJobs_Source]
        UNIQUE ([SourceDocumentId],[SourceDocumentType],[SourceVersion]),
    CONSTRAINT [FK_SalesReportingJobs_Business]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_SalesReportingJobs_Source]
        FOREIGN KEY ([SourceDocumentProcessingJobId])
        REFERENCES [dbo].[DocumentProcessingJobs]([JobId]),
    CONSTRAINT [CK_SalesReportingJobs_SourceVersion] CHECK ([SourceVersion]>0),
    CONSTRAINT [CK_SalesReportingJobs_SourceShape] CHECK
      (([SourceDocumentProcessingJobId] IS NOT NULL AND [SourcePayloadJson] IS NULL) OR
       ([SourceDocumentProcessingJobId] IS NULL AND ISJSON([SourcePayloadJson])=1)),
    CONSTRAINT [CK_SalesReportingJobs_Status]
        CHECK ([Status] IN (N'Pending',N'Processing',N'Projected',N'Failed')),
    CONSTRAINT [CK_SalesReportingJobs_Attempts] CHECK ([AttemptCount]>=0)
);
GO

CREATE INDEX [IX_SalesReportingJobs_Dispatch]
    ON [reporting].[SalesReportingJobs]([BusinessId],[Status],[CreatedAt])
    INCLUDE([SourceDocumentId],[SourceDocumentType],[AttemptCount]);
GO

CREATE TABLE [reporting].[CommercialReportVisitFacts]
(
    [RouteVisitId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [VisitDate] DATE NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [RouteId] UNIQUEIDENTIFIER NOT NULL,
    [RouteCode] NVARCHAR(32) NOT NULL,
    [RouteName] NVARCHAR(160) NOT NULL,
    [ZoneId] UNIQUEIDENTIFIER NULL,
    [ZoneName] NVARCHAR(160) NULL,
    [SellerId] UNIQUEIDENTIFIER NOT NULL,
    [SellerName] NVARCHAR(240) NOT NULL,
    [RouteStopId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerName] NVARCHAR(240) NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [HasOrder] BIT NOT NULL,
    [OrderId] UNIQUEIDENTIFIER NULL,
    [SkipReason] NVARCHAR(300) NULL,
    [VisitObservation] NVARCHAR(1000) NULL,
    [RecordedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [SourceVersion] BIGINT NOT NULL,
    [ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_CommercialReportVisitFacts] PRIMARY KEY ([RouteVisitId]),
    CONSTRAINT [FK_CommercialReportVisitFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_CommercialReportVisitFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_CommercialReportVisitFacts_Status] CHECK ([Status] IN (N'Visited',N'Skipped')),
    CONSTRAINT [CK_CommercialReportVisitFacts_Order] CHECK (([HasOrder]=1 AND [OrderId] IS NOT NULL) OR ([HasOrder]=0 AND [OrderId] IS NULL))
);
GO
CREATE INDEX [IX_CommercialReportVisitFacts_Business_Date_Seller]
  ON [reporting].[CommercialReportVisitFacts]([BusinessId],[VisitDate] DESC,[SellerId])
  INCLUDE([RouteId],[CustomerId],[Status],[HasOrder],[OrderId]);
GO

CREATE TABLE [reporting].[CommercialReportOrderFacts] (
  [OrderId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[BusinessId] UNIQUEIDENTIFIER NOT NULL,
  [CreatedDate] DATE NOT NULL,[CreatedAt] DATETIMEOFFSET(7) NOT NULL,[OrderNumber] NVARCHAR(300) NOT NULL,
  [SellerId] UNIQUEIDENTIFIER NOT NULL,[SellerName] NVARCHAR(240) NOT NULL,[CustomerId] UNIQUEIDENTIFIER NOT NULL,
  [CustomerName] NVARCHAR(240) NOT NULL,[RouteId] UNIQUEIDENTIFIER NULL,[RouteName] NVARCHAR(160) NULL,
  [ZoneId] UNIQUEIDENTIFIER NULL,[ZoneName] NVARCHAR(160) NULL,[RouteStopId] UNIQUEIDENTIFIER NULL,[PartySiteId] UNIQUEIDENTIFIER NULL,
  [SourceChannel] NVARCHAR(32) NOT NULL CONSTRAINT [DF_CommercialReportOrderFacts_Channel] DEFAULT N'SellerOrder',
  [CapturedOffline] BIT NOT NULL CONSTRAINT [DF_CommercialReportOrderFacts_Offline] DEFAULT 0,
  [TotalAmount] DECIMAL(19,4) NOT NULL,
  [Status] INT NOT NULL,[RequiresStockReview] BIT NOT NULL,[ProjectionVersion] SMALLINT NOT NULL,[ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
  [SourceVersion] BIGINT NOT NULL CONSTRAINT [DF_CommercialReportOrderFacts_SourceVersion] DEFAULT 1,
  [ConfirmedAt] DATETIMEOFFSET(7) NULL,[CancelledAt] DATETIMEOFFSET(7) NULL,
  [InvoiceDocumentId] UNIQUEIDENTIFIER NULL,[InvoicedAt] DATETIMEOFFSET(7) NULL,
  CONSTRAINT [FK_CommercialReportOrderFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
  CONSTRAINT [FK_CommercialReportOrderFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
  CONSTRAINT [CK_CommercialReportOrderFacts_SourceVersion] CHECK ([SourceVersion]>0)
);
GO
CREATE INDEX [IX_CommercialReportOrderFacts_Business_Date_Seller]
ON [reporting].[CommercialReportOrderFacts]([BusinessId],[CreatedDate] DESC,[SellerId]) INCLUDE([Status],[TotalAmount],[CustomerId],[RouteId],[ZoneId],[InvoiceDocumentId]);
GO

CREATE TABLE [reporting].[CommercialCoverageAssignmentFacts] (
  [CoverageAssignmentFactId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  [TenantId] UNIQUEIDENTIFIER NOT NULL,[BusinessId] UNIQUEIDENTIFIER NOT NULL,
  [RouteId] UNIQUEIDENTIFIER NOT NULL,[RouteCode] NVARCHAR(32) NOT NULL,[RouteName] NVARCHAR(160) NOT NULL,
  [RouteScheduleId] UNIQUEIDENTIFIER NOT NULL,[DayOfWeek] TINYINT NOT NULL,[RunOrder] INT NOT NULL,[PlannedStartTime] TIME(0) NULL,
  [ZoneId] UNIQUEIDENTIFIER NULL,[ZoneName] NVARCHAR(160) NULL,
  [SellerId] UNIQUEIDENTIFIER NOT NULL,[SellerName] NVARCHAR(240) NOT NULL,
  [RouteStopId] UNIQUEIDENTIFIER NOT NULL,[CustomerId] UNIQUEIDENTIFIER NOT NULL,[CustomerName] NVARCHAR(240) NOT NULL,
  [PartySiteId] UNIQUEIDENTIFIER NOT NULL,[PartySiteName] NVARCHAR(200) NOT NULL,[Sequence] INT NOT NULL,[PlannedVisitTime] TIME(0) NULL,
  [CityName] NVARCHAR(160) NULL,[Neighborhood] NVARCHAR(160) NULL,[Latitude] DECIMAL(9,6) NULL,[Longitude] DECIMAL(9,6) NULL,
  [TimeZoneId] NVARCHAR(100) NOT NULL,[ValidFromBusinessDate] DATE NOT NULL,[ValidToBusinessDateExclusive] DATE NULL,
  [SourceVersion] BIGINT NOT NULL,[ProjectionVersion] SMALLINT NOT NULL,[ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
  CONSTRAINT [FK_CommercialCoverageAssignmentFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
  CONSTRAINT [FK_CommercialCoverageAssignmentFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
  CONSTRAINT [UQ_CommercialCoverageAssignmentFacts_Validity] UNIQUE([BusinessId],[RouteScheduleId],[RouteStopId],[ValidFromBusinessDate]),
  CONSTRAINT [CK_CommercialCoverageAssignmentFacts_Day] CHECK([DayOfWeek] BETWEEN 1 AND 7),
  CONSTRAINT [CK_CommercialCoverageAssignmentFacts_Validity] CHECK([ValidToBusinessDateExclusive] IS NULL OR [ValidToBusinessDateExclusive]>[ValidFromBusinessDate]),
  CONSTRAINT [CK_CommercialCoverageAssignmentFacts_Version] CHECK([SourceVersion]>0 AND [ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_CommercialCoverageAssignmentFacts_Query]
ON [reporting].[CommercialCoverageAssignmentFacts]([BusinessId],[ValidFromBusinessDate],[ValidToBusinessDateExclusive],[DayOfWeek])
INCLUDE([SellerId],[RouteId],[ZoneId],[CustomerId],[PartySiteId]);
GO

CREATE TABLE [reporting].[PurchaseReportDocuments] (
  [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[BusinessId] UNIQUEIDENTIFIER NOT NULL,
  [SourceDocumentType] NVARCHAR(32) NOT NULL,[OriginalGoodsReceiptId] UNIQUEIDENTIFIER NULL,[DocumentNumber] NVARCHAR(64) NOT NULL,
  [OccurredAt] DATETIMEOFFSET(7) NOT NULL,[BusinessLocalDate] DATE NOT NULL,[TimeZoneId] NVARCHAR(100) NOT NULL,
  [SupplierId] UNIQUEIDENTIFIER NOT NULL,[SupplierName] NVARCHAR(240) NOT NULL,[WarehouseId] UNIQUEIDENTIFIER NOT NULL,[WarehouseName] NVARCHAR(200) NOT NULL,
  [CurrencyCode] CHAR(3) NOT NULL,[NetAmount] DECIMAL(19,4) NOT NULL,[TaxAmount] DECIMAL(19,4) NOT NULL,[TotalAmount] DECIMAL(19,4) NOT NULL,
  [ProjectionVersion] SMALLINT NOT NULL,[ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
  CONSTRAINT [FK_PurchaseReportDocuments_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
  CONSTRAINT [FK_PurchaseReportDocuments_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
  CONSTRAINT [CK_PurchaseReportDocuments_Type] CHECK([SourceDocumentType] IN(N'GoodsReceipt',N'PurchaseReturn')),
  CONSTRAINT [CK_PurchaseReportDocuments_Sign] CHECK(([SourceDocumentType]=N'GoodsReceipt' AND [TotalAmount]>=0) OR ([SourceDocumentType]=N'PurchaseReturn' AND [TotalAmount]<=0))
);
GO
CREATE INDEX [IX_PurchaseReportDocuments_Query]
ON [reporting].[PurchaseReportDocuments]([BusinessId],[BusinessLocalDate],[SupplierId]) INCLUDE([WarehouseId],[NetAmount],[TaxAmount],[TotalAmount]);
GO

CREATE TABLE [reporting].[PurchaseReportLineFacts] (
  [PurchaseFactId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[BusinessId] UNIQUEIDENTIFIER NOT NULL,
  [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,[SourceDocumentType] NVARCHAR(32) NOT NULL,[SourceLineNumber] INT NOT NULL,
  [OriginalGoodsReceiptId] UNIQUEIDENTIFIER NULL,[OriginalLineNumber] INT NULL,[OccurredAt] DATETIMEOFFSET(7) NOT NULL,[BusinessLocalDate] DATE NOT NULL,
  [SupplierId] UNIQUEIDENTIFIER NOT NULL,[SupplierName] NVARCHAR(240) NOT NULL,[WarehouseId] UNIQUEIDENTIFIER NOT NULL,[WarehouseName] NVARCHAR(200) NOT NULL,
  [ProductId] UNIQUEIDENTIFIER NOT NULL,[ProductName] NVARCHAR(300) NOT NULL,[Quantity] DECIMAL(19,6) NOT NULL,
  [UnitCost] DECIMAL(19,6) NOT NULL,[DiscountAmount] DECIMAL(19,4) NOT NULL,[NetAmount] DECIMAL(19,4) NOT NULL,
  [TaxAmount] DECIMAL(19,4) NOT NULL,[TotalAmount] DECIMAL(19,4) NOT NULL,[CurrencyCode] CHAR(3) NOT NULL,
  [ProjectionVersion] SMALLINT NOT NULL,[ProjectedAt] DATETIMEOFFSET(7) NOT NULL,
  CONSTRAINT [FK_PurchaseReportLineFacts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
  CONSTRAINT [FK_PurchaseReportLineFacts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
  CONSTRAINT [UQ_PurchaseReportLineFacts_Source] UNIQUE([SourceDocumentId],[SourceDocumentType],[SourceLineNumber]),
  CONSTRAINT [CK_PurchaseReportLineFacts_Type] CHECK([SourceDocumentType] IN(N'GoodsReceipt',N'PurchaseReturn')),
  CONSTRAINT [CK_PurchaseReportLineFacts_Sign] CHECK(([SourceDocumentType]=N'GoodsReceipt' AND [Quantity]>0 AND [TotalAmount]>=0) OR ([SourceDocumentType]=N'PurchaseReturn' AND [Quantity]<0 AND [TotalAmount]<=0))
);
GO
CREATE INDEX [IX_PurchaseReportLineFacts_Query]
ON [reporting].[PurchaseReportLineFacts]([BusinessId],[BusinessLocalDate],[SupplierId]) INCLUDE([WarehouseId],[ProductId],[Quantity],[NetAmount],[TotalAmount]);
GO
