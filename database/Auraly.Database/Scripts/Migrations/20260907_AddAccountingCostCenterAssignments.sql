IF OBJECT_ID(N'dbo.AccountingCostCenterAssignments',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountingCostCenterAssignments
    (
        AssignmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AccountingCostCenterAssignments PRIMARY KEY,
        TenantId UNIQUEIDENTIFIER NOT NULL,
        BusinessId UNIQUEIDENTIFIER NOT NULL,
        CostCenterId UNIQUEIDENTIFIER NOT NULL,
        OperationKind NVARCHAR(32) NOT NULL,
        WarehouseId UNIQUEIDENTIFIER NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_AccountingCostCenterAssignments_IsActive DEFAULT (1),
        CreatedAt DATETIMEOFFSET(7) NOT NULL,
        UpdatedAt DATETIMEOFFSET(7) NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_AccountingCostCenterAssignments_Rule UNIQUE (BusinessId,OperationKind,WarehouseId),
        CONSTRAINT FK_AccountingCostCenterAssignments_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId),
        CONSTRAINT FK_AccountingCostCenterAssignments_Business FOREIGN KEY (BusinessId) REFERENCES dbo.Businesses(BusinessId),
        CONSTRAINT FK_AccountingCostCenterAssignments_Center FOREIGN KEY (BusinessId,CostCenterId) REFERENCES dbo.AccountingCostCenters(BusinessId,CostCenterId),
        CONSTRAINT FK_AccountingCostCenterAssignments_Warehouse FOREIGN KEY (BusinessId,WarehouseId) REFERENCES dbo.Warehouses(BusinessId,WarehouseId),
        CONSTRAINT CK_AccountingCostCenterAssignments_Kind CHECK (OperationKind IN (N'All',N'Sales',N'Purchasing',N'Expenses',N'Inventory'))
    );
END;
GO

IF COL_LENGTH(N'dbo.AccountingPostingJobs',N'ResolvedCostCenterId') IS NULL
BEGIN
    ALTER TABLE dbo.AccountingPostingJobs ADD ResolvedCostCenterId UNIQUEIDENTIFIER NULL;
    ALTER TABLE dbo.AccountingPostingJobs ADD CONSTRAINT FK_AccountingPostingJobs_ResolvedCostCenter
      FOREIGN KEY (BusinessId,ResolvedCostCenterId) REFERENCES dbo.AccountingCostCenters(BusinessId,CostCenterId);
END;
GO

IF COL_LENGTH(N'dbo.AccountingEntryLines',N'PartyIdentificationSnapshot') IS NULL
    ALTER TABLE dbo.AccountingEntryLines ADD PartyIdentificationSnapshot NVARCHAR(80) NULL;
IF COL_LENGTH(N'dbo.AccountingEntryLines',N'PartyNameSnapshot') IS NULL
    ALTER TABLE dbo.AccountingEntryLines ADD PartyNameSnapshot NVARCHAR(240) NULL;
IF COL_LENGTH(N'dbo.AccountingEntryLines',N'CostCenterCodeSnapshot') IS NULL
    ALTER TABLE dbo.AccountingEntryLines ADD CostCenterCodeSnapshot NVARCHAR(32) NULL;
IF COL_LENGTH(N'dbo.AccountingEntryLines',N'CostCenterNameSnapshot') IS NULL
    ALTER TABLE dbo.AccountingEntryLines ADD CostCenterNameSnapshot NVARCHAR(160) NULL;
GO

UPDATE line
SET PartyIdentificationSnapshot=party.Identification,
    PartyNameSnapshot=party.DisplayName,
    CostCenterCodeSnapshot=center.Code,
    CostCenterNameSnapshot=center.Name
FROM dbo.AccountingEntryLines line
LEFT JOIN dbo.Parties party ON party.PartyId=line.PartyId
LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=line.CostCenterId
WHERE (line.PartyId IS NOT NULL AND
       (line.PartyIdentificationSnapshot IS NULL OR line.PartyNameSnapshot IS NULL))
   OR (line.CostCenterId IS NOT NULL AND
       (line.CostCenterCodeSnapshot IS NULL OR line.CostCenterNameSnapshot IS NULL));
GO
