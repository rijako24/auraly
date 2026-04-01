-- =============================================================================
-- VERIFICACION: confirmar que la BD está alineada con commit 487bf5e2
-- (migraciones hasta 028, sin 029-041).
-- Todas las filas deben decir OK.
-- =============================================================================

SET NOCOUNT ON;

;WITH checks AS (
    -- AgentId NO debe existir
    SELECT N'031 Conversations.AgentId (NO debe existir)' AS chk,
           CASE WHEN NOT EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'AgentId'
           ) THEN N'OK' ELSE N'FAIL' END AS resultado

    -- Columnas legacy DEBEN existir
    UNION ALL SELECT N'032 Conversations.State (DEBE existir)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'State'
           ) THEN N'OK' ELSE N'FAIL' END

    UNION ALL SELECT N'032 Conversations.LastIntent (DEBE existir)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastIntent'
           ) THEN N'OK' ELSE N'FAIL' END

    UNION ALL SELECT N'032 Conversations.BabyAge (DEBE existir)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'BabyAge'
           ) THEN N'OK' ELSE N'FAIL' END

    UNION ALL SELECT N'032 Conversations.RecommendedPlan (DEBE existir)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'RecommendedPlan'
           ) THEN N'OK' ELSE N'FAIL' END

    UNION ALL SELECT N'032 IX_Conversations_State (DEBE existir)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'IX_Conversations_State'
           ) THEN N'OK' ELSE N'FAIL' END

    -- LastMessage DEBE existir (Tables/Conversations.sql + entidad Conversation en 487bf5e2)
    UNION ALL SELECT N'Conversations.LastMessage (DEBE existir — EF)',
           CASE WHEN EXISTS (
               SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastMessage'
           ) THEN N'OK' ELSE N'FAIL' END

    -- Permisos agents NO deben existir
    UNION ALL SELECT N'034 permiso agents.read (NO debe existir)',
           CASE WHEN NOT EXISTS (
               SELECT 1 FROM dbo.Permissions WHERE Resource = N'agents.read'
           ) THEN N'OK' ELSE N'FAIL' END

    UNION ALL SELECT N'034 permiso agents.write (NO debe existir)',
           CASE WHEN NOT EXISTS (
               SELECT 1 FROM dbo.Permissions WHERE Resource = N'agents.write'
           ) THEN N'OK' ELSE N'FAIL' END

    -- Tabla FlowNodeCatalog NO debe existir
    UNION ALL SELECT N'035/036 tabla FlowNodeCatalog (NO debe existir)',
           CASE WHEN NOT EXISTS (
               SELECT 1 FROM sys.tables
               WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FlowNodeCatalog'
           ) THEN N'OK' ELSE N'FAIL' END
)
SELECT chk AS Comprobacion, resultado
FROM checks
ORDER BY chk;
