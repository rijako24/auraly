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

-- Asignar a todos los roles Administrator del sistema
INSERT INTO [dbo].[RolePermissions] ([RolePermissionId], [RoleId], [PermissionId], [AssignedAt])
SELECT NEWID(), r.[RoleId], p.[PermissionId], GETUTCDATE()
FROM [dbo].[AppRoles] r
INNER JOIN [dbo].[Permissions] p ON p.[Resource] IN (N'agents.read', N'agents.update', N'catalog.import')
WHERE r.[NormalizedName] = N'ADMINISTRATOR'
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[RolePermissions] rp
    WHERE rp.[RoleId] = r.[RoleId] AND rp.[PermissionId] = p.[PermissionId]
  );

PRINT N'SeedAgentPermissions: agents.read / agents.update listos.';
GO
