CREATE TABLE [catalog].[ProductColors] (
    [ProductColorId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [SortOrder] INT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductColors] PRIMARY KEY ([ProductColorId]),
    CONSTRAINT [FK_ProductColors_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_ProductColors_Tenant_Id] UNIQUE ([TenantId], [ProductColorId]),
    CONSTRAINT [UQ_ProductColors_Tenant_Code] UNIQUE ([TenantId], [Code]),
    CONSTRAINT [UQ_ProductColors_Tenant_Name] UNIQUE ([TenantId], [Name])
);
