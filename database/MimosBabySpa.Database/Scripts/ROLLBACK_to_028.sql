-- =============================================================================
-- ROLLBACK: Revertir migraciones 029-038 para dejar la BD alineada con commit
-- 487bf5e2 (Motor generico estable, 19-mar-2026).
--
-- Ejecutar sobre la BD restaurada del backup 20-mar-2026 09:31.
-- Idempotente: cada bloque verifica antes de actuar.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

PRINT '=== ROLLBACK: inicio ===';

-- ─── Revertir 031: eliminar Conversations.AgentId ────────────────────────────

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_Conversations_Agents'
      AND parent_object_id = OBJECT_ID(N'dbo.Conversations')
)
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP CONSTRAINT [FK_Conversations_Agents];
    PRINT '[031] FK_Conversations_Agents eliminada.';
END

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'IX_Conversations_AgentId'
)
BEGIN
    DROP INDEX [IX_Conversations_AgentId] ON [dbo].[Conversations];
    PRINT '[031] IX_Conversations_AgentId eliminado.';
END

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'AgentId'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP COLUMN [AgentId];
    PRINT '[031] Columna AgentId eliminada de Conversations.';
END
ELSE
    PRINT '[031] AgentId ya no existe — OK.';

-- ─── Revertir 032: restaurar columnas legacy eliminadas ──────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastIntent'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [LastIntent] NVARCHAR(50) NULL;
    PRINT '[032-revert] Columna LastIntent restaurada.';
END
ELSE
    PRINT '[032-revert] LastIntent ya existe — OK.';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'BabyAge'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [BabyAge] INT NULL;
    PRINT '[032-revert] Columna BabyAge restaurada.';
END
ELSE
    PRINT '[032-revert] BabyAge ya existe — OK.';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'RecommendedPlan'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [RecommendedPlan] NVARCHAR(100) NULL;
    PRINT '[032-revert] Columna RecommendedPlan restaurada.';
END
ELSE
    PRINT '[032-revert] RecommendedPlan ya existe — OK.';

-- State ya existe (OK en la validación), pero verificamos el índice
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'IX_Conversations_State'
)
BEGIN
    CREATE INDEX [IX_Conversations_State] ON [dbo].[Conversations] ([State]);
    PRINT '[032-revert] IX_Conversations_State recreado.';
END
ELSE
    PRINT '[032-revert] IX_Conversations_State ya existe — OK.';

-- ─── Revertir 034: eliminar permisos agents ──────────────────────────────────

DELETE rp
FROM [dbo].[RolePermissions] rp
INNER JOIN [dbo].[Permissions] p ON rp.[PermissionId] = p.[PermissionId]
WHERE p.[Resource] IN (N'agents.read', N'agents.write');

IF @@ROWCOUNT > 0
    PRINT '[034] RolePermissions de agents eliminados.';
ELSE
    PRINT '[034] No había RolePermissions de agents.';

DELETE FROM [dbo].[Permissions]
WHERE [Resource] IN (N'agents.read', N'agents.write');

IF @@ROWCOUNT > 0
    PRINT '[034] Permisos agents.read / agents.write eliminados.';
ELSE
    PRINT '[034] No había permisos agents — OK.';

-- ─── Revertir 035/036: eliminar tabla FlowNodeCatalog ────────────────────────

IF EXISTS (
    SELECT 1 FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FlowNodeCatalog'
)
BEGIN
    DROP TABLE [dbo].[FlowNodeCatalog];
    PRINT '[035/036] Tabla FlowNodeCatalog eliminada.';
END
ELSE
    PRINT '[035/036] FlowNodeCatalog no existe — OK.';

-- ─── Alinear con Tables/Conversations.sql y EF (Conversation.LastMessage) ─────
-- El script 033 no está en esta rama, pero el esquema del proyecto y la entidad
-- sí incluyen LastMessage; sin esta columna la app falla al consultar conversaciones.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastMessage'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [LastMessage] NVARCHAR(1000) NULL;
    PRINT '[schema] Columna LastMessage añadida (alineado con Conversations.sql).';
END
ELSE
    PRINT '[schema] LastMessage ya existe — OK.';

PRINT '=== ROLLBACK: completado ===';

COMMIT TRANSACTION;
GO
