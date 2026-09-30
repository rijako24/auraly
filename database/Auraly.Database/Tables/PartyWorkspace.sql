CREATE TABLE [dbo].[SupplierCreationReceipts] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SupplierCreationReceipts] PRIMARY KEY ([BusinessId], [OperationId]),
    CONSTRAINT [FK_SupplierCreationReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierCreationReceipts_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId])
);
GO

GO
CREATE TABLE [dbo].[Carriers] (
    [CarrierId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [TransportationMode] NVARCHAR(24) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Carriers] PRIMARY KEY ([CarrierId]),
    CONSTRAINT [FK_Carriers_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_Carriers_Parties] FOREIGN KEY ([TenantId], [PartyId]) REFERENCES [dbo].[Parties] ([TenantId], [PartyId]),
    CONSTRAINT [UQ_Carriers_Tenant_Party] UNIQUE ([TenantId],[PartyId]),
    CONSTRAINT [UQ_Carriers_Tenant_Code] UNIQUE ([TenantId],[Code]),
    CONSTRAINT [CK_Carriers_Mode] CHECK ([TransportationMode] IN (N'Road',N'Air',N'Maritime',N'Other'))
);
GO
CREATE TABLE [dbo].[CommercialRoleCreationReceipts] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [RoleType] NVARCHAR(16) NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_CommercialRoleCreationReceipts] PRIMARY KEY ([BusinessId],[OperationId]),
    CONSTRAINT [FK_CommercialRoleCreationReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_CommercialRoleCreationReceipts_Type] CHECK ([RoleType] IN (N'Seller',N'Carrier'))
);
