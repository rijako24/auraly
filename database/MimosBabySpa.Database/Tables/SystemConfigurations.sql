CREATE TABLE [dbo].[SystemConfigurations] (
    [SystemConfigurationId] INT NOT NULL PRIMARY KEY,
    [Value] NVARCHAR(MAX) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL
);

GO
