CREATE TABLE [catalog].[ProductSizes] (
    [ProductSizeId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [SortOrder] INT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductSizes] PRIMARY KEY ([ProductSizeId]),
    CONSTRAINT [FK_ProductSizes_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_ProductSizes_Tenant_Id] UNIQUE ([TenantId], [ProductSizeId]),
    CONSTRAINT [UQ_ProductSizes_Tenant_Code] UNIQUE ([TenantId], [Code]),
    CONSTRAINT [UQ_ProductSizes_Tenant_Name] UNIQUE ([TenantId], [Name])
);
