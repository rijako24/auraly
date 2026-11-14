-- Coordinated pre-DACPAC cutover, with old API/Function writers stopped.
-- The five DEV identities and codes were reviewed and approved on 2026-11-13.
-- No journal, document, balance, parent or explicit cost-center reference changes.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.AccountingCostCenters',N'BusinessId') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'    IF EXISTS(SELECT 1 FROM dbo.AccountingCostCenterAssignments WHERE WarehouseId IS NOT NULL)
        THROW 51769,N''Revisa las reglas por bodega antes de compartir los centros; el corte no las elimina ni elige una regla ganadora.'',1;

    DECLARE @ReviewedCenters TABLE(CostCenterId uniqueidentifier PRIMARY KEY,
        BusinessId uniqueidentifier,NewCode nvarchar(32),KeepDefault bit);
    IF N''$(DeploymentEnvironment)''=N''dev''
        INSERT @ReviewedCenters VALUES
        (''098314c8-6dd6-48c4-bd5f-fa7a735020dc'',''a0a10000-0000-0000-0000-000000000001'',N''PRINCIPAL'',1),
        (''5233cf30-5f28-40ac-8181-1fc50f215976'',''b24a3ab4-a226-4dab-bc0c-4d634e34c3fb'',N''PRINCIPAL-BAR'',0),
        (''6c83cbbb-e811-4a6b-accf-19cb73ab88a0'',''587de932-8c0f-4c99-9f5c-da651a2f3fd4'',N''PRINCIPAL-RESTBAR'',0),
        (''bb3a84fd-178a-4dd0-9e4b-4663d5d60e13'',''2b8de478-3bce-4f28-b66d-3e53e5dc2c36'',N''PRINCIPAL-TIENDA'',0),
        (''44bfed0f-9e7e-45ba-989f-a31ea8776fe2'',''969d7ff9-8464-4337-b927-8f520978af33'',N''PRINCIPAL-TIENDA-DEV'',0);

    -- Validate the resulting global catalog before writing any configuration.
    DECLARE @Result TABLE(CostCenterId uniqueidentifier PRIMARY KEY,TenantId uniqueidentifier,
        BusinessId uniqueidentifier,Code nvarchar(32),IsDefault bit,IsActive bit);
    INSERT @Result
    SELECT c.CostCenterId,b.TenantId,c.BusinessId,COALESCE(reviewed.NewCode,c.Code),
        COALESCE(reviewed.KeepDefault,c.IsDefault),c.IsActive
    FROM dbo.AccountingCostCenters c WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
    LEFT JOIN @ReviewedCenters reviewed ON reviewed.CostCenterId=c.CostCenterId AND reviewed.BusinessId=c.BusinessId
        AND b.TenantId=''a0a10000-0000-0000-0000-000000000000''
        AND c.Code IN(N''PRINCIPAL'',reviewed.NewCode) AND c.Name=N''Operación principal'' AND c.IsActive=1;
    IF EXISTS(SELECT TenantId,Code FROM @Result GROUP BY TenantId,Code HAVING COUNT(*)>1)
        THROW 51769,N''Hay códigos de centros repetidos en una empresa. Se requiere un plan revisado; no se fusionan ni renombran centros automáticamente.'',1;
    IF EXISTS(SELECT TenantId FROM @Result WHERE IsDefault=1 AND IsActive=1 GROUP BY TenantId HAVING COUNT(*)>1)
        THROW 51769,N''Hay varios centros predeterminados en una empresa; revisa explícitamente el centro general antes del corte.'',1;
    IF EXISTS(SELECT 1 FROM dbo.AccountingCostCenterAssignments assignment JOIN @Result center ON center.BusinessId=assignment.BusinessId
        JOIN dbo.AccountingCostCenters previous ON previous.CostCenterId=center.CostCenterId
        WHERE previous.IsDefault=1 AND previous.IsActive=1 AND assignment.OperationKind=N''All''
          AND assignment.IsActive=0)
        THROW 51769,N''Una regla general inactiva impide conservar el predeterminado de la sede. Revisa la configuración antes del corte.'',1;

    -- Reuse existing All rules to retain each site''s former fallback. Explicit
    -- active rules already take priority and remain unchanged.
    INSERT dbo.AccountingCostCenterAssignments
        (AssignmentId,TenantId,BusinessId,CostCenterId,OperationKind,WarehouseId,IsActive,CreatedAt,UpdatedAt)
    SELECT NEWID(),projected.TenantId,projected.BusinessId,previous.CostCenterId,N''All'',NULL,1,SYSUTCDATETIME(),SYSUTCDATETIME()
    FROM dbo.AccountingCostCenters previous JOIN @Result projected ON projected.CostCenterId=previous.CostCenterId
    WHERE previous.IsDefault=1 AND previous.IsActive=1 AND NOT EXISTS(
        SELECT 1 FROM dbo.AccountingCostCenterAssignments assignment
        WHERE assignment.BusinessId=previous.BusinessId AND assignment.OperationKind=N''All'');
    UPDATE previous SET Code=projected.Code,IsDefault=projected.IsDefault
    FROM dbo.AccountingCostCenters previous JOIN @Result projected ON projected.CostCenterId=previous.CostCenterId
    WHERE previous.Code<>projected.Code OR previous.IsDefault<>projected.IsDefault;

    IF COL_LENGTH(N''dbo.AccountingCostCenters'',N''TenantId'') IS NULL
        EXEC sys.sp_executesql N''ALTER TABLE dbo.AccountingCostCenters ADD TenantId uniqueidentifier NULL;'';
    EXEC sys.sp_executesql N''UPDATE center SET TenantId=business.TenantId
        FROM dbo.AccountingCostCenters center JOIN dbo.Businesses business ON business.BusinessId=center.BusinessId
        WHERE center.TenantId IS NULL;
        IF EXISTS(SELECT 1 FROM dbo.AccountingCostCenters center JOIN dbo.Businesses business ON business.BusinessId=center.BusinessId
          WHERE center.TenantId<>business.TenantId)
          THROW 51769,N''''El tenant del centro no coincide con su sede histórica.'''',1;
        IF EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WHERE TenantId IS NULL)
          THROW 51769,N''''No se pudo determinar la empresa de todos los centros históricos.'''',1;
        IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''''dbo.AccountingCostCenters'''') AND name=N''''TenantId'''' AND is_nullable=1)
          ALTER TABLE dbo.AccountingCostCenters ALTER COLUMN TenantId uniqueidentifier NOT NULL;'';';
    -- Drop only the canonical old composite relationships. DropObjectsNotInSource
    -- deliberately preserves unknown customer objects, so the DACPAC cannot
    -- remove these obsolete keys implicitly before changing the archived scope.
    -- Compatible writers remain stopped until all new tenant FKs are installed.
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AccountingCostCenters_Parent' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenters'))
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT FK_AccountingCostCenters_Parent;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AccountingCostCenterAssignments_Center' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenterAssignments'))
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT FK_AccountingCostCenterAssignments_Center;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AccountingPostingJobs_ResolvedCostCenter' AND parent_object_id=OBJECT_ID(N'dbo.AccountingPostingJobs'))
        ALTER TABLE dbo.AccountingPostingJobs DROP CONSTRAINT FK_AccountingPostingJobs_ResolvedCostCenter;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExpenseConcepts_CostCenters' AND parent_object_id=OBJECT_ID(N'dbo.ExpenseConcepts'))
        ALTER TABLE dbo.ExpenseConcepts DROP CONSTRAINT FK_ExpenseConcepts_CostCenters;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Expenses_CostCenters' AND parent_object_id=OBJECT_ID(N'dbo.Expenses'))
        ALTER TABLE dbo.Expenses DROP CONSTRAINT FK_Expenses_CostCenters;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AccountingCostCenters_Businesses' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenters'))
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT FK_AccountingCostCenters_Businesses;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AccountingCostCenterAssignments_Warehouse' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenterAssignments'))
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT FK_AccountingCostCenterAssignments_Warehouse;
    IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_AccountingCostCenters_Business_Center' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenters'))
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT UQ_AccountingCostCenters_Business_Center;
    IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_AccountingCostCenters_Business_Code' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenters'))
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT UQ_AccountingCostCenters_Business_Code;
    IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_AccountingCostCenterAssignments_Rule' AND parent_object_id=OBJECT_ID(N'dbo.AccountingCostCenterAssignments'))
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT UQ_AccountingCostCenterAssignments_Rule;

    -- SQL Server will not rename BusinessId while a filtered index references
    -- it. New writers stay stopped until the DACPAC installs the tenant index.
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AccountingCostCenters')
        AND name=N'UX_AccountingCostCenters_Business_Default')
        DROP INDEX UX_AccountingCostCenters_Business_Default ON dbo.AccountingCostCenters;
END;
COMMIT TRANSACTION;
GO
