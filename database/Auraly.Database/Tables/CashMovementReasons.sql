CREATE TABLE [dbo].[CashMovementReasons]
(
    [ReasonId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [Direction] NVARCHAR(8) NOT NULL,
    [CounterpartAccountingCategory] NVARCHAR(64) NULL,
    [DefaultCostCenterId] UNIQUEIDENTIFIER NULL,
    [RequiresReference] BIT NOT NULL CONSTRAINT [DF_CashMovementReasons_RequiresReference] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_CashMovementReasons_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashMovementReasons] PRIMARY KEY CLUSTERED ([ReasonId]),
    CONSTRAINT [UQ_CashMovementReasons_Business_Reason] UNIQUE ([BusinessId],[ReasonId]),
    CONSTRAINT [UQ_CashMovementReasons_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [FK_CashMovementReasons_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CashMovementReasons_CostCenters] FOREIGN KEY ([DefaultCostCenterId])
        REFERENCES [dbo].[AccountingCostCenters] ([CostCenterId]),
    CONSTRAINT [CK_CashMovementReasons_Direction] CHECK ([Direction] IN (N'In',N'Out'))
);
GO

CREATE INDEX [IX_CashMovementReasons_Business_Direction]
    ON [dbo].[CashMovementReasons] ([BusinessId],[Direction],[IsActive])
    INCLUDE ([Code],[Name],[CounterpartAccountingCategory],[DefaultCostCenterId]);
GO
