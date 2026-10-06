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



PRINT N'SeedAgentPermissions: catálogo de agentes listo.';

GO

