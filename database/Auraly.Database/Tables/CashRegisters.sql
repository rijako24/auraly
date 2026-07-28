CREATE TABLE [dbo].[CashRegisters]
(
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_CashRegisters_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_CashRegisters] PRIMARY KEY CLUSTERED ([RegisterId]),
    CONSTRAINT [FK_CashRegisters_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CashRegisters_BusinessLocations] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[BusinessLocations] ([LocationId]),
    CONSTRAINT [FK_CashRegisters_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [UQ_CashRegisters_Business_Code] UNIQUE ([BusinessId], [Code])
);

GO

CREATE INDEX [IX_CashRegisters_Business_Warehouse]
    ON [dbo].[CashRegisters] ([BusinessId], [WarehouseId]);

