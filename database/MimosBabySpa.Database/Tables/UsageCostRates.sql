CREATE TABLE [dbo].[UsageCostRates] (
    [UsageCostRateId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_UsageCostRates] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Code]            NVARCHAR(100)    NOT NULL,
    [OperationType]   INT              NOT NULL,
    [Unit]            NVARCHAR(50)     NOT NULL,
    [CostUsd]         DECIMAL(18, 8)   NOT NULL DEFAULT 0,
    [CostCop]         DECIMAL(18, 4)   NOT NULL DEFAULT 0,
    [EffectiveFrom]   DATETIME2        NOT NULL,
    [EffectiveTo]     DATETIME2        NULL,
    [IsActive]        BIT              NOT NULL DEFAULT 1,
    [CreatedAt]       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);

GO

CREATE INDEX [IX_UsageCostRates_Code_Type_From] ON [dbo].[UsageCostRates] ([Code], [OperationType], [EffectiveFrom]);
