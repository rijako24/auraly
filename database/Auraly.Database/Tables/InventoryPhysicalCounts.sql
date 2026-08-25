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
    CONSTRAINT [CK_InventoryPhysicalCounts_Status] CHECK ([Status] IN (N'Draft',N'PreCounting',N'Counting',N'Review',N'Closing',N'Closed',N'Cancelled')),
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
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountLists] PRIMARY KEY ([InventoryPhysicalCountListId]),
    CONSTRAINT [FK_InventoryPhysicalCountLists_Count] FOREIGN KEY ([InventoryPhysicalCountId]) REFERENCES [dbo].[InventoryPhysicalCounts]([InventoryPhysicalCountId]),
    CONSTRAINT [FK_InventoryPhysicalCountLists_User] FOREIGN KEY ([AssignedUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InventoryPhysicalCountLists_Status] CHECK ([Status] IN (N'Pending',N'PreCounting',N'PreCounted',N'Counting',N'Counted')),
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
    [CountedQuantity] DECIMAL(19,6) NULL,
    [CountedByUserId] UNIQUEIDENTIFIER NULL,
    [CountedAt] DATETIMEOFFSET(7) NULL,
    [CountedAtProcessingSequence] BIGINT NULL,
    [ExpectedQuantityAtCount] DECIMAL(19,6) NULL,
    [ApprovedDifference] DECIMAL(19,6) NULL,
    [IsExcluded] BIT NOT NULL CONSTRAINT [DF_InventoryPhysicalCountLines_IsExcluded] DEFAULT (0),
    [ExclusionReason] NVARCHAR(250) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryPhysicalCountLines] PRIMARY KEY ([InventoryPhysicalCountId],[ProductId]),
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
