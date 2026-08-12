CREATE TABLE [dbo].[InventoryReasons]
(
    [InventoryReasonId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OperationType] NVARCHAR(64) NOT NULL,
    [Code] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_InventoryReasons_IsSystem] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_InventoryReasons_IsActive] DEFAULT (1),
    [DisplayOrder] INT NOT NULL CONSTRAINT [DF_InventoryReasons_DisplayOrder] DEFAULT (0),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryReasons] PRIMARY KEY CLUSTERED ([InventoryReasonId]),
    CONSTRAINT [FK_InventoryReasons_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_InventoryReasons_OperationType] CHECK ([OperationType] IN (N'StockCount',N'InventoryAdjustment',N'WarehouseTransfer',N'ProductConversion',N'Damage')),
    CONSTRAINT [CK_InventoryReasons_DisplayOrder] CHECK ([DisplayOrder] BETWEEN 0 AND 9999)
);
GO
CREATE UNIQUE INDEX [UX_InventoryReasons_Business_Operation_Code]
    ON [dbo].[InventoryReasons]([BusinessId],[OperationType],[Code]);
GO
CREATE UNIQUE INDEX [UX_InventoryReasons_Business_Operation_Name]
    ON [dbo].[InventoryReasons]([BusinessId],[OperationType],[Name]);
GO
CREATE INDEX [IX_InventoryReasons_Business_Active]
    ON [dbo].[InventoryReasons]([BusinessId],[OperationType],[IsActive],[DisplayOrder]);
GO
