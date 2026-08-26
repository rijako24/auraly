CREATE TABLE [dbo].[InventoryPhysicalCounts]
(
    [InventoryPhysicalCountId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ScopeType] NVARCHAR(16) NOT NULL,
    [ReasonCode] NVARCHAR(40) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [BaseInventorySequence] BIGINT NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NULL,
    [ReviewStartedAt] DATETIMEOFFSET(7) NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [FinalInventoryOperationId] UNIQUEIDENTIFIER NULL,
    [FinalDocumentNumber] NVARCHAR(40) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCounts] PRIMARY KEY ([InventoryPhysicalCountId]),
    CONSTRAINT [FK_InventoryPhysicalCounts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_InventoryPhysicalCounts_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_InventoryPhysicalCounts_User] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InventoryPhysicalCounts_Scope] CHECK ([ScopeType] IN (N'General',N'Partial')),
    CONSTRAINT [CK_InventoryPhysicalCounts_Status] CHECK ([Status] IN (N'Open',N'Reconciling',N'Closing',N'Closed',N'Cancelled',N'Draft',N'PreCounting',N'Counting',N'Review')),
    CONSTRAINT [CK_InventoryPhysicalCounts_Final] CHECK (([Status]=N'Closed' AND [FinalInventoryOperationId] IS NOT NULL AND [FinalDocumentNumber] IS NOT NULL) OR [Status]<>N'Closed')
);
GO
CREATE INDEX [IX_InventoryPhysicalCounts_Business_Status]
    ON [dbo].[InventoryPhysicalCounts]([BusinessId],[Status],[CreatedAt] DESC)
    INCLUDE([WarehouseId],[ScopeType],[FinalDocumentNumber]);
GO

CREATE TABLE [dbo].[InventoryPhysicalCountLists]
(
    [InventoryPhysicalCountListId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryPhysicalCountId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [AssignedUserId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [PreCountSubmittedAt] DATETIMEOFFSET(7) NULL,
    [CountSubmittedAt] DATETIMEOFFSET(7) NULL,
    [Version] BIGINT NOT NULL CONSTRAINT [DF_InventoryPhysicalCountLists_Version] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_InventoryPhysicalCountLists_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_InventoryPhysicalCountLists_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountLists] PRIMARY KEY ([InventoryPhysicalCountListId]),
    CONSTRAINT [FK_InventoryPhysicalCountLists_Count] FOREIGN KEY ([InventoryPhysicalCountId]) REFERENCES [dbo].[InventoryPhysicalCounts]([InventoryPhysicalCountId]),
    CONSTRAINT [FK_InventoryPhysicalCountLists_User] FOREIGN KEY ([AssignedUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InventoryPhysicalCountLists_Status] CHECK ([Status] IN (N'InProgress',N'Ready',N'Discarded',N'Pending',N'PreCounting',N'PreCounted',N'Counting',N'Counted')),
    CONSTRAINT [UQ_InventoryPhysicalCountLists_Name] UNIQUE ([InventoryPhysicalCountId],[Name])
);
GO
CREATE INDEX [IX_InventoryPhysicalCountLists_Count_Status]
    ON [dbo].[InventoryPhysicalCountLists]([InventoryPhysicalCountId],[Status]);
GO

CREATE TABLE [dbo].[InventoryPhysicalCountLines]
(
    [InventoryPhysicalCountId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryPhysicalCountListId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCodeSnapshot] NVARCHAR(80) NOT NULL,
    [ProductNameSnapshot] NVARCHAR(250) NOT NULL,
    [SystemQuantityAtBase] DECIMAL(19,6) NOT NULL,
    [PreCountQuantity] DECIMAL(19,6) NULL,
    [PreCountedByUserId] UNIQUEIDENTIFIER NULL,
    [PreCountedAt] DATETIMEOFFSET(7) NULL,
    [PreCountedAtProcessingSequence] BIGINT NULL,
    [CountedQuantity] DECIMAL(19,6) NULL,
    [CountedByUserId] UNIQUEIDENTIFIER NULL,
    [CountedAt] DATETIMEOFFSET(7) NULL,
    [CountedAtProcessingSequence] BIGINT NULL,
    [ExpectedQuantityAtCount] DECIMAL(19,6) NULL,
    [ApprovedDifference] DECIMAL(19,6) NULL,
    [IsExcluded] BIT NOT NULL CONSTRAINT [DF_InventoryPhysicalCountLines_IsExcluded] DEFAULT (0),
    [ExclusionReason] NVARCHAR(250) NULL,
    [PendingReason] NVARCHAR(250) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountLines] PRIMARY KEY ([InventoryPhysicalCountListId],[ProductId]),
    CONSTRAINT [FK_InventoryPhysicalCountLines_Count] FOREIGN KEY ([InventoryPhysicalCountId]) REFERENCES [dbo].[InventoryPhysicalCounts]([InventoryPhysicalCountId]),
    CONSTRAINT [FK_InventoryPhysicalCountLines_List] FOREIGN KEY ([InventoryPhysicalCountListId]) REFERENCES [dbo].[InventoryPhysicalCountLists]([InventoryPhysicalCountListId]),
    CONSTRAINT [FK_InventoryPhysicalCountLines_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [FK_InventoryPhysicalCountLines_PreCountUser] FOREIGN KEY ([PreCountedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_InventoryPhysicalCountLines_CountUser] FOREIGN KEY ([CountedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InventoryPhysicalCountLines_PreCount] CHECK ([PreCountQuantity] IS NULL OR [PreCountQuantity]>=0),
    CONSTRAINT [CK_InventoryPhysicalCountLines_Count] CHECK ([CountedQuantity] IS NULL OR [CountedQuantity]>=0),
    CONSTRAINT [CK_InventoryPhysicalCountLines_Excluded] CHECK (([IsExcluded]=0 AND [ExclusionReason] IS NULL) OR ([IsExcluded]=1 AND [ExclusionReason] IS NOT NULL))
);
GO
CREATE INDEX [IX_InventoryPhysicalCountLines_List]
    ON [dbo].[InventoryPhysicalCountLines]([InventoryPhysicalCountListId],[ProductNameSnapshot]);
GO
CREATE INDEX [IX_InventoryPhysicalCountLines_Count_Product]
    ON [dbo].[InventoryPhysicalCountLines]([InventoryPhysicalCountId],[ProductId]);
GO

CREATE TABLE [dbo].[InventoryPhysicalCountReconciliations]
(
    [InventoryPhysicalCountReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryPhysicalCountId] UNIQUEIDENTIFIER NOT NULL,
    [SnapshotInventorySequence] BIGINT NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [AppliedAt] DATETIMEOFFSET(7) NULL,
    [CountedProductCount] INT NOT NULL,
    [UncountedProductCount] INT NOT NULL,
    [CountedApplicationStatus] NVARCHAR(24) NULL,
    [CountedDocumentId] UNIQUEIDENTIFIER NULL,
    [CountedDocumentNumber] NVARCHAR(40) NULL,
    [UncountedApplicationStatus] NVARCHAR(24) NULL,
    [UncountedDocumentId] UNIQUEIDENTIFIER NULL,
    [UncountedDocumentNumber] NVARCHAR(40) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountReconciliations] PRIMARY KEY ([InventoryPhysicalCountReconciliationId]),
    CONSTRAINT [FK_InventoryPhysicalCountReconciliations_Count] FOREIGN KEY ([InventoryPhysicalCountId]) REFERENCES [dbo].[InventoryPhysicalCounts]([InventoryPhysicalCountId]),
    CONSTRAINT [FK_InventoryPhysicalCountReconciliations_User] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InventoryPhysicalCountReconciliations_Status] CHECK ([Status] IN (N'Active',N'Superseded',N'Applied')),
    CONSTRAINT [CK_InventoryPhysicalCountReconciliations_Counts] CHECK ([CountedProductCount]>=0 AND [UncountedProductCount]>=0),
    CONSTRAINT [CK_InventoryPhysicalCountReconciliations_Applications] CHECK (
        ([CountedApplicationStatus] IS NULL OR [CountedApplicationStatus] IN (N'Processing',N'Applied',N'Failed')) AND
        ([UncountedApplicationStatus] IS NULL OR [UncountedApplicationStatus] IN (N'Processing',N'Applied',N'Failed')))
);
GO
CREATE UNIQUE INDEX [UX_InventoryPhysicalCountReconciliations_Active]
    ON [dbo].[InventoryPhysicalCountReconciliations]([InventoryPhysicalCountId]) WHERE [Status]=N'Active';
GO

CREATE TABLE [dbo].[InventoryPhysicalCountReconciliationDrafts]
(
    [InventoryPhysicalCountReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryPhysicalCountListId] UNIQUEIDENTIFIER NOT NULL,
    [DraftVersion] BIGINT NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountReconciliationDrafts] PRIMARY KEY ([InventoryPhysicalCountReconciliationId],[InventoryPhysicalCountListId]),
    CONSTRAINT [FK_InventoryPhysicalCountReconciliationDrafts_Reconciliation] FOREIGN KEY ([InventoryPhysicalCountReconciliationId]) REFERENCES [dbo].[InventoryPhysicalCountReconciliations]([InventoryPhysicalCountReconciliationId]),
    CONSTRAINT [FK_InventoryPhysicalCountReconciliationDrafts_Draft] FOREIGN KEY ([InventoryPhysicalCountListId]) REFERENCES [dbo].[InventoryPhysicalCountLists]([InventoryPhysicalCountListId])
);
GO
