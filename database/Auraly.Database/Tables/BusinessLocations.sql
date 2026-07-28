CREATE TABLE [dbo].[BusinessLocations]
(
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_BusinessLocations_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_BusinessLocations] PRIMARY KEY CLUSTERED ([LocationId]),
    CONSTRAINT [FK_BusinessLocations_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_BusinessLocations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_BusinessLocations_Tenant_Business_Code] UNIQUE ([TenantId], [BusinessId], [Code])
);

GO

CREATE INDEX [IX_BusinessLocations_Tenant_Business]
    ON [dbo].[BusinessLocations] ([TenantId], [BusinessId]);

