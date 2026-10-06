-- Ejecutar una sola vez al deshabilitar Atención y crecimiento en un ambiente.
-- Conserva el catálogo para futuras asignaciones expresas; no se incluye en
-- PostDeployment porque una publicación posterior no debe retirar esos opt-ins.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE assignment
FROM dbo.RolePermissions assignment
JOIN dbo.Permissions permissionValue
  ON permissionValue.PermissionId = assignment.PermissionId
WHERE permissionValue.Resource LIKE N'agents.%'
   OR permissionValue.Resource LIKE N'conversations.%'
   OR permissionValue.Resource LIKE N'leads.%'
   OR permissionValue.Resource LIKE N'campaigns.%'
   OR permissionValue.Resource LIKE N'reservations.%';

SELECT @@ROWCOUNT AS RemovedRoleGrants;

COMMIT TRANSACTION;
