CREATE TABLE [dbo].[Employees] (
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Employees_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Employees_Parties] FOREIGN KEY ([TenantId], [PartyId]) REFERENCES [dbo].[Parties] ([TenantId], [PartyId]),
    CONSTRAINT [UQ_Employees_Tenant_Employee] UNIQUE ([TenantId], [EmployeeId])
);

GO

CREATE INDEX [IX_Employees_TenantId] ON [dbo].[Employees] ([TenantId]);

GO

CREATE INDEX [IX_Employees_TenantId_Name] ON [dbo].[Employees] ([TenantId], [Name]);
GO

CREATE UNIQUE INDEX [UX_Employees_TenantId_PartyId] ON [dbo].[Employees] ([TenantId], [PartyId]) WHERE [PartyId] IS NOT NULL;


GO
