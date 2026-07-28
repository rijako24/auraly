CREATE TABLE [dbo].[PosDevicePermissions]
(
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionCode] NVARCHAR(128) NOT NULL,
    [IsGranted] BIT NOT NULL,
    [GrantedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PosDevicePermissions] PRIMARY KEY CLUSTERED ([DeviceId], [PermissionCode]),
    CONSTRAINT [FK_PosDevicePermissions_PosDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[PosDevices] ([DeviceId])
);

