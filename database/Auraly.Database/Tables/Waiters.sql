CREATE TABLE [parties].[Waiters]
(
    [WaiterId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [ShortName] NVARCHAR(80) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Waiters] PRIMARY KEY ([WaiterId]),
    CONSTRAINT [UQ_Waiters_Tenant_Party] UNIQUE ([TenantId],[PartyId]),
    CONSTRAINT [UQ_Waiters_Tenant_Code] UNIQUE ([TenantId],[Code]),
    CONSTRAINT [UQ_Waiters_Tenant_Id] UNIQUE ([TenantId],[WaiterId]),
    CONSTRAINT [FK_Waiters_Parties] FOREIGN KEY ([TenantId],[PartyId])
        REFERENCES [dbo].[Parties] ([TenantId],[PartyId])
);
GO
