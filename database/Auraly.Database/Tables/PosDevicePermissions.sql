-- Compatibilidad de despliegue: la versión anterior de la API todavía puede leer
-- esta tabla mientras el nuevo binario reemplaza la instancia. La API actual no
-- la consulta ni la escribe. Retirar físicamente en un release posterior, cuando
-- ya no exista posibilidad de rollback al esquema de autorización por dispositivo.
CREATE TABLE [dbo].[PosDevicePermissions]
(
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionCode] NVARCHAR(128) NOT NULL,
    [IsGranted] BIT NOT NULL,
    [GrantedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PosDevicePermissions] PRIMARY KEY CLUSTERED ([DeviceId], [PermissionCode]),
    CONSTRAINT [FK_PosDevicePermissions_EnrolledDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId])
);
