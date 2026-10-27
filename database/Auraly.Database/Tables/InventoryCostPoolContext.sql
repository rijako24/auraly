-- Immutable context for provenance. InitialQuantity is a historical cut, never
-- an editable stock balance. Current stock/value remain in InventoryBalances.
CREATE TABLE [inventory].[CostPoolEpochs]
(
    [EpochId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ScopeKind] NVARCHAR(24) NOT NULL,
    [IsDamageWarehouse] BIT NOT NULL,
    [MemberBusinessIdsJson] NVARCHAR(MAX) NOT NULL,
    [InitialQuantity] DECIMAL(28,6) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_InventoryCostPoolEpochs] PRIMARY KEY ([EpochId]),
    CONSTRAINT [UQ_InventoryCostPoolEpochs_ProductScope] UNIQUE ([EpochId],[ProductId],[ScopeKind]),
    CONSTRAINT [UQ_InventoryCostPoolEpochs_ProductDamage] UNIQUE ([EpochId],[ProductId],[IsDamageWarehouse]),
    CONSTRAINT [FK_InventoryCostPoolEpochs_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_InventoryCostPoolEpochs_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [FK_InventoryCostPoolEpochs_Source] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType])
        REFERENCES [dbo].[DocumentProcessingJobs]([DocumentId],[DocumentType]),
    CONSTRAINT [CK_InventoryCostPoolEpochs_Scope] CHECK (
        ([ScopeKind] IN(N'Business',N'SharedTenant') AND [IsDamageWarehouse]=0)
        OR ([ScopeKind]=N'DamageWarehouse' AND [IsDamageWarehouse]=1)),
    CONSTRAINT [CK_InventoryCostPoolEpochs_Members] CHECK (
        ISJSON([MemberBusinessIdsJson])=1 AND LEFT(LTRIM([MemberBusinessIdsJson]),1)=N'[')
);
GO

-- A context reference and an observed existing sequence, not a processing
-- cursor: this table never allocates work, increments sequences or owns stock.
CREATE TABLE [inventory].[CostPoolHeads]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [IsDamageWarehouse] BIT NOT NULL,
    [EpochId] UNIQUEIDENTIFIER NOT NULL,
    [LastProcessingSequence] BIGINT NOT NULL,
    CONSTRAINT [PK_InventoryCostPoolHeads] PRIMARY KEY ([BusinessId],[ProductId],[IsDamageWarehouse]),
    CONSTRAINT [FK_InventoryCostPoolHeads_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_InventoryCostPoolHeads_Epoch] FOREIGN KEY ([EpochId],[ProductId],[IsDamageWarehouse])
        REFERENCES [inventory].[CostPoolEpochs]([EpochId],[ProductId],[IsDamageWarehouse]),
    CONSTRAINT [CK_InventoryCostPoolHeads_Sequence] CHECK ([LastProcessingSequence]>=0)
);
