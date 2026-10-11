CREATE TABLE [sales].[RestaurantCommandConfigurations]
(
    [ConfigurationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [StationId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Mode] NVARCHAR(16) NOT NULL,
    [PrinterDeviceId] UNIQUEIDENTIFIER NULL,
    [PrinterProfileKey] NVARCHAR(64) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantCommandConfigurations] PRIMARY KEY ([ConfigurationId]),
    CONSTRAINT [UQ_RestaurantCommandConfigurations_Station] UNIQUE ([BusinessId],[StationId]),
    CONSTRAINT [UQ_RestaurantCommandConfigurations_Scope] UNIQUE ([BusinessId],[StationId],[ConfigurationId]),
    CONSTRAINT [FK_RestaurantCommandConfigurations_Station] FOREIGN KEY ([BusinessId],[StationId])
        REFERENCES [sales].[RestaurantStations] ([BusinessId],[StationId]),
    CONSTRAINT [FK_RestaurantCommandConfigurations_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantCommandConfigurations_Name] CHECK (LEN(LTRIM(RTRIM([Name])))>0),
    CONSTRAINT [CK_RestaurantCommandConfigurations_Mode] CHECK ([Mode] IN (N'Virtual',N'Physical',N'Both')),
    CONSTRAINT [CK_RestaurantCommandConfigurations_Printer] CHECK
        (([Mode]=N'Virtual' AND [PrinterDeviceId] IS NULL AND [PrinterProfileKey] IS NULL)
         OR ([Mode] IN (N'Physical',N'Both') AND [PrinterDeviceId] IS NOT NULL
             AND [PrinterProfileKey] IS NOT NULL))
);
