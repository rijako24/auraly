-- Permisos del módulo Agente IA (admin). Idempotente.

SET NOCOUNT ON;



DECLARE @Perms TABLE (Module NVARCHAR(50), Action NVARCHAR(50), Resource NVARCHAR(100), Description NVARCHAR(500));

INSERT INTO @Perms VALUES

(N'Agents', N'Read', N'agents.read', N'Ver agentes IA'),

(N'Agents', N'Update', N'agents.update', N'Configurar agente IA'),

(N'Catalog', N'Import', N'catalog.import', N'Importar catálogo desde documento');



INSERT INTO [dbo].[Permissions] ([PermissionId], [Module], [Action], [Resource], [Description], [CreatedAt])

SELECT NEWID(), p.Module, p.Action, p.Resource, p.Description, GETUTCDATE()

FROM @Perms p

WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Resource] = p.Resource);



-- Regla canónica: todo administrador recibe los permisos disponibles dentro de
-- su alcance. La contratación del módulo se controla por entitlement, no quitando
-- permisos al rol Administrador.
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),roleValue.RoleId,permissionValue.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles roleValue
CROSS JOIN dbo.Permissions permissionValue
WHERE roleValue.IsActive=1
  AND roleValue.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR')
  AND permissionValue.Resource IN(N'agents.read',N'agents.update')
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions existing
      WHERE existing.RoleId=roleValue.RoleId
        AND existing.PermissionId=permissionValue.PermissionId);

PRINT N'SeedAgentPermissions: catálogo de agentes listo.';

GO

