SET NOCOUNT ON;

-- Roles iniciales coherentes para tenants nuevos y existentes.
UPDATE dbo.AppRoles
SET Name=N'Administrador',Description=N'Administración completa de la empresa y todas sus sedes.',IsSystemRole=1
WHERE NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR');

UPDATE dbo.AppRoles
SET IsSystemRole=0
WHERE NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'ADMINISTRATIVE');

INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
SELECT NEWID(),tenant.TenantId,preset.Name,preset.NormalizedName,preset.Description,1,preset.IsSystemRole,SYSUTCDATETIME()
FROM dbo.Tenants tenant
CROSS JOIN (VALUES
  (N'Cajero',N'CASHIER',N'Operación de venta cotidiana sin acciones sensibles.',CAST(0 AS bit)),
  (N'Supervisor',N'SUPERVISOR',N'Supervisión operativa y autorización de acciones sensibles.',CAST(0 AS bit)),
  (N'Administrativo',N'ADMINISTRATIVE',N'Administración comercial y operativa del tenant.',CAST(0 AS bit)),
  (N'Administrador',N'ADMINISTRATOR',N'Administración completa de la empresa y todas sus sedes.',CAST(1 AS bit))
) preset(Name,NormalizedName,Description,IsSystemRole)
WHERE tenant.IsActive=1
  AND NOT EXISTS(
    SELECT 1 FROM dbo.AppRoles existing
    WHERE existing.TenantId=tenant.TenantId
      AND (existing.NormalizedName=preset.NormalizedName
        OR preset.NormalizedName=N'ADMINISTRATOR' AND existing.NormalizedName=N'TENANTADMINISTRATOR'));

-- Elimina permisos impropios de las tres plantillas operativas y conserva una matriz determinista.
DELETE assignment
FROM dbo.RolePermissions assignment
JOIN dbo.AppRoles roleValue ON roleValue.RoleId=assignment.RoleId
JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=assignment.PermissionId
WHERE roleValue.NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'ADMINISTRATIVE')
  AND NOT (
    roleValue.NormalizedName=N'CASHIER' AND permissionValue.Resource IN(
      N'sales.create',N'sales.reprint',N'pos.customer.create',N'pos.orders',N'orders.read',
      N'work-sessions.read',N'work-sessions.open',N'work-sessions.cash.manage',N'work-sessions.cash.drawer.open')
    OR roleValue.NormalizedName=N'SUPERVISOR' AND permissionValue.Resource IN(
      N'sales.create',N'sales.discount',N'sales.reprint',N'sales.lines.remove',N'sales.drafts.restart',
      N'pos.approvals.authorize',N'pos.approvals.read',N'pos.approvals.receive_notifications',N'pos.approvals.manage_credential',N'pos.workspace.change',
      N'pos.customer.create',N'pos.orders',N'orders.read',N'orders.invoice',
      N'sales.returns.read',N'sales.returns.create',N'sales.returns.confirm',
      N'work-sessions.read',N'work-sessions.open',N'work-sessions.close',N'work-sessions.cash.manage',N'work-sessions.cash.drawer.open',
      N'inventory.read',N'inventory.costs.read',
      N'inventory.counts.confirm',N'inventory.adjustments.confirm',N'inventory.transfers.confirm',
      N'inventory.conversions.confirm',N'inventory.damages.confirm')
    OR roleValue.NormalizedName=N'ADMINISTRATIVE' AND permissionValue.Resource NOT LIKE N'tenants.%'
      AND permissionValue.Resource NOT LIKE N'roles.%'
      AND permissionValue.Resource NOT LIKE N'users.%'
      AND permissionValue.Resource NOT LIKE N'audit[_]logs.%'
  );

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),roleValue.RoleId,permissionValue.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles roleValue
CROSS JOIN dbo.Permissions permissionValue
WHERE roleValue.IsActive=1
  AND (
    roleValue.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR')
      AND (permissionValue.Resource NOT LIKE N'tenants.%' AND permissionValue.Resource NOT LIKE N'platform.%'
        OR EXISTS(SELECT 1 FROM dbo.Tenants ownerTenant WHERE ownerTenant.TenantId=roleValue.TenantId AND ownerTenant.TenantKey=N'@auraly'))
    OR roleValue.NormalizedName=N'CASHIER' AND permissionValue.Resource IN(
      N'sales.create',N'sales.reprint',N'pos.customer.create',N'pos.orders',N'orders.read',
      N'work-sessions.read',N'work-sessions.open',N'work-sessions.cash.manage',N'work-sessions.cash.drawer.open')
    OR roleValue.NormalizedName=N'SUPERVISOR' AND permissionValue.Resource IN(
      N'sales.create',N'sales.discount',N'sales.reprint',N'sales.lines.remove',N'sales.drafts.restart',
      N'pos.approvals.authorize',N'pos.approvals.read',N'pos.approvals.receive_notifications',N'pos.approvals.manage_credential',N'pos.workspace.change',
      N'pos.customer.create',N'pos.orders',N'orders.read',N'orders.invoice',
      N'sales.returns.read',N'sales.returns.create',N'sales.returns.confirm',
      N'work-sessions.read',N'work-sessions.open',N'work-sessions.close',N'work-sessions.cash.manage',N'work-sessions.cash.drawer.open',
      N'inventory.read',N'inventory.costs.read',
      N'inventory.counts.confirm',N'inventory.adjustments.confirm',N'inventory.transfers.confirm',
      N'inventory.conversions.confirm',N'inventory.damages.confirm')
    OR roleValue.NormalizedName=N'ADMINISTRATIVE' AND permissionValue.Resource NOT LIKE N'tenants.%'
      AND permissionValue.Resource NOT LIKE N'roles.%'
      AND permissionValue.Resource NOT LIKE N'users.%'
      AND permissionValue.Resource NOT LIKE N'audit[_]logs.%'
  )
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions existing WHERE existing.RoleId=roleValue.RoleId AND existing.PermissionId=permissionValue.PermissionId);
