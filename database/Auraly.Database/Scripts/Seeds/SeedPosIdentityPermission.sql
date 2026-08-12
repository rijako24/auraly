DECLARE @PosIdentitySyncPermissionId UNIQUEIDENTIFIER =
    CAST('019B2A31-7B93-7B4A-873B-C07C4AB9D99F' AS UNIQUEIDENTIFIER);

IF NOT EXISTS (
    SELECT 1 FROM dbo.Permissions WHERE Resource=N'pos.identity.sync')
BEGIN
    INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description)
    VALUES(
        @PosIdentitySyncPermissionId,
        N'POS',
        N'SyncIdentity',
        N'pos.identity.sync',
        N'Sincronizar usuarios y permisos mínimos en un dispositivo POS enrolado');
END
ELSE
BEGIN
    SELECT @PosIdentitySyncPermissionId=PermissionId
    FROM dbo.Permissions WHERE Resource=N'pos.identity.sync';
END;

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId)
SELECT NEWID(),r.RoleId,@PosIdentitySyncPermissionId
FROM dbo.AppRoles r
WHERE r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId
        AND rp.PermissionId=@PosIdentitySyncPermissionId);

DECLARE @PosCashierPermissions TABLE(
    Module NVARCHAR(50) NOT NULL,
    Action NVARCHAR(50) NOT NULL,
    Resource NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NOT NULL);
INSERT @PosCashierPermissions(Module,Action,Resource,Description)
VALUES
    (N'Sales',N'Create',N'sales.create',N'Crear y emitir ventas desde una caja'),
    (N'Sales',N'Discount',N'sales.discount',N'Aplicar descuentos en líneas de venta'),
    (N'Sales',N'Reprint',N'sales.reprint',N'Reimprimir facturas con trazabilidad'),
    (N'Sales',N'Void',N'sales.void',N'Eliminar líneas o reiniciar ventas');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PosCashierPermissions p
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=p.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p
  ON p.Resource IN(N'sales.create',N'sales.discount',N'sales.reprint',N'sales.void')
WHERE r.IsActive=1
  AND r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
GO
