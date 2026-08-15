CREATE TABLE [dbo].[Tenants] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [Name] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [MaximumUsers] INT NOT NULL CONSTRAINT [DF_Tenants_MaximumUsers] DEFAULT (5),
    [MaximumEnrolledDevices] INT NOT NULL CONSTRAINT [DF_Tenants_MaximumEnrolledDevices] DEFAULT (1),
    CONSTRAINT [CK_Tenants_MaximumUsers] CHECK ([MaximumUsers] >= 1),
    CONSTRAINT [CK_Tenants_MaximumEnrolledDevices] CHECK ([MaximumEnrolledDevices] >= 0),
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL
);

GO

CREATE UNIQUE INDEX [IX_Tenants_Email] ON [dbo].[Tenants] ([Email]);

GO
