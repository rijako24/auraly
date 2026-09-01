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



-- Agente y atención son capacidades opt-in para empresas cliente. El administrador
-- de plataforma conserva el catálogo completo; en los demás tenants se asignan
-- explícitamente desde Roles cuando el producto contratado las requiera.
DELETE assignment
FROM dbo.RolePermissions assignment
JOIN dbo.AppRoles roleValue ON roleValue.RoleId=assignment.RoleId
JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=roleValue.TenantId
JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=assignment.PermissionId
WHERE roleValue.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR')
  AND tenantValue.TenantKey<>N'@auraly'
  AND permissionValue.Resource IN(N'agents.read',N'agents.update');

PRINT N'SeedAgentPermissions: catálogo opt-in de agentes listo.';

GO

