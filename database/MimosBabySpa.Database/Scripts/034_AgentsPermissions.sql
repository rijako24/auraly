-- =============================================================================
-- 034: Permisos de Agents (admin UI + API)
-- Ejecutar en entornos existentes si ya tienes datos; el seed en WebAPI también
-- inserta permisos desde PermissionCatalog al arrancar.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @Perms TABLE (Module NVARCHAR(50), Action NVARCHAR(50), Resource NVARCHAR(100), Description NVARCHAR(500));
INSERT INTO @Perms VALUES
(N'Agents', N'Read', N'agents.read', N'Ver agentes y flujos'),
(N'Agents', N'Write', N'agents.write', N'Crear y editar agentes, flujos y playground');

INSERT INTO [dbo].[Permissions] ([PermissionId], [Module], [Action], [Resource], [Description], [CreatedAt])
SELECT NEWID(), p.Module, p.Action, p.Resource, p.Description, GETUTCDATE()
FROM @Perms p WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Resource] = p.Resource);

-- Asignar a todos los roles Administrator por tenant (mismo patrón que SeedAdminUser)
INSERT INTO [dbo].[RolePermissions] ([RolePermissionId], [RoleId], [PermissionId], [AssignedAt])
SELECT NEWID(), r.[RoleId], p.[PermissionId], GETUTCDATE()
FROM [dbo].[AppRoles] r
CROSS JOIN [dbo].[Permissions] p
WHERE r.[NormalizedName] = N'ADMINISTRATOR'
  AND p.[Resource] IN (N'agents.read', N'agents.write')
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[RolePermissions] rp
    WHERE rp.[RoleId] = r.[RoleId] AND rp.[PermissionId] = p.[PermissionId]
  );
