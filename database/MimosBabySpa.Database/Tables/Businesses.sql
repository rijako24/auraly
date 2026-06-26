CREATE TABLE [dbo].[Businesses] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(2000) NOT NULL,
    [Address] NVARCHAR(500) NOT NULL,
    [Phone] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [Website] NVARCHAR(500) NOT NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [TimeZone] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Businesses_TimeZone] DEFAULT N'America/Bogota',
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Businesses_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Businesses_TenantId] ON [dbo].[Businesses] ([TenantId]);

GO
