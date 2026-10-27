-- Derived open quantity detail per physical warehouse/product, written only by
-- the inventory ledger. It is never an alternative stock/value authority.
CREATE TABLE [inventory].[OriginPositions]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ContextId] UNIQUEIDENTIFIER NOT NULL,
    [OriginId] UNIQUEIDENTIFIER NOT NULL,
    [SourceMovementId] UNIQUEIDENTIFIER NULL,
    [RecordedAt] DATETIMEOFFSET(7) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    CONSTRAINT [PK_InventoryOriginPositions] PRIMARY KEY CLUSTERED
      ([BusinessId],[WarehouseId],[ProductId],[ContextId],[OriginId]),
    CONSTRAINT [FK_InventoryOriginPositions_Balance] FOREIGN KEY ([BusinessId],[WarehouseId],[ProductId])
      REFERENCES [dbo].[InventoryBalances]([BusinessId],[WarehouseId],[ProductId]),
    CONSTRAINT [FK_InventoryOriginPositions_Source] FOREIGN KEY ([SourceMovementId])
      REFERENCES [dbo].[InventoryMovements]([InventoryMovementId]),
    CONSTRAINT [CK_InventoryOriginPositions_Quantity] CHECK ([Quantity]>0)
);
GO

-- Immutable facts of each movement. A referenced issue identifies a return;
-- its restored quantity is aggregated from these facts, without another total.
CREATE TABLE [inventory].[MovementOrigins]
(
    [InventoryMovementId] UNIQUEIDENTIFIER NOT NULL,
    [OriginId] UNIQUEIDENTIFIER NOT NULL,
    [SourceMovementId] UNIQUEIDENTIFIER NULL,
    [RecordedAt] DATETIMEOFFSET(7) NOT NULL,
    [QuantityChange] DECIMAL(19,6) NOT NULL,
    [ReferencedMovementId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_InventoryMovementOrigins] PRIMARY KEY CLUSTERED ([InventoryMovementId],[OriginId]),
    CONSTRAINT [FK_InventoryMovementOrigins_Movement] FOREIGN KEY ([InventoryMovementId])
      REFERENCES [dbo].[InventoryMovements]([InventoryMovementId]),
    CONSTRAINT [FK_InventoryMovementOrigins_Source] FOREIGN KEY ([SourceMovementId])
      REFERENCES [dbo].[InventoryMovements]([InventoryMovementId]),
    CONSTRAINT [FK_InventoryMovementOrigins_Referenced] FOREIGN KEY ([ReferencedMovementId])
      REFERENCES [dbo].[InventoryMovements]([InventoryMovementId]),
    CONSTRAINT [CK_InventoryMovementOrigins_Quantity] CHECK ([QuantityChange]<>0)
);
GO
CREATE INDEX [IX_InventoryMovementOrigins_Referenced]
  ON [inventory].[MovementOrigins]([ReferencedMovementId],[OriginId]) INCLUDE ([QuantityChange]);
GO
