IF COL_LENGTH(N'dbo.Tenants', N'InventoryCostBasis') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants
        ADD InventoryCostBasis NVARCHAR(32) NOT NULL
            CONSTRAINT DF_Tenants_InventoryCostBasis
            DEFAULT N'LatestReceiptCost' WITH VALUES;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Tenants')
      AND name = N'CK_Tenants_InventoryCostBasis')
BEGIN
    ALTER TABLE dbo.Tenants WITH CHECK
        ADD CONSTRAINT CK_Tenants_InventoryCostBasis
        CHECK (InventoryCostBasis IN (N'LatestReceiptCost', N'WeightedAverageCost'));
END;
GO
