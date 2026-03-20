-- Extract (8) and Agent (10) catalog entries.
-- Agent is a cluster node: supports subNodes (extract, actions, knowledge, event).
-- Extract node is deprecated in cluster-node architecture (kept for legacy compatibility).
SET NOCOUNT ON;

MERGE INTO [dbo].[FlowNodeCatalog] AS tgt
USING (VALUES
    (N'extract', N'Extracción (IA) [legado]', 8, N'scan-search', N'Entrada', N'#6366f1',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"default","label":"Siguiente"}]',
     N'{"type":"object","properties":{},"additionalProperties":false,"description":"[Legado] En cluster-node architecture la extracción se hace dentro de cada Agent vía sub-nodo Extract."}',
     5),
    (N'agent', N'Agente (cluster)', 10, N'brain-circuit', N'IA', N'#a855f7',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"completed","label":"Completado"},{"id":"failure","label":"Error"},{"id":"received","label":"Evento (waitForEvent)"}]',
     N'{"type":"object","x-isCompound":true,"x-allowedSlots":["extract","action","knowledge","event"],"properties":{"completionBehavior":{"type":"string","title":"Comportamiento al completar pipeline","enum":["advance","respond"],"default":"advance"},"completionPort":{"type":"string","title":"Puerto al completar pipeline","default":"completed"},"actionPipeline":{"type":"array","title":"Pasos de acción (legado — usar subNodes.actions)","items":{"type":"object","additionalProperties":true}},"responseMode":{"type":"string","enum":["llm","template"]},"instructions":{"type":"string"},"waitForUser":{"type":"boolean","default":true},"knowledgeSourceIds":{"type":"array","items":{"type":"string"}},"collect":{"type":"object","properties":{"fields":{"type":"array","items":{"type":"string"}},"instructions":{"type":"string"},"knowledgeSourceIds":{"type":"array","items":{"type":"string"}}}}},"additionalProperties":true}',
     6)
) AS src (
    [CatalogKey], [Name], [FlowNodeType], [Icon], [Category], [Color],
    [InputsJson], [OutputsJson], [ConfigSchemaJson], [DisplayOrder]
)
ON tgt.[CatalogKey] = src.[CatalogKey]
WHEN MATCHED THEN UPDATE SET
    [Name] = src.[Name],
    [FlowNodeType] = src.[FlowNodeType],
    [Icon] = src.[Icon],
    [Category] = src.[Category],
    [Color] = src.[Color],
    [InputsJson] = src.[InputsJson],
    [OutputsJson] = src.[OutputsJson],
    [ConfigSchemaJson] = src.[ConfigSchemaJson],
    [DisplayOrder] = src.[DisplayOrder],
    [IsActive] = 1,
    [UpdatedAt] = GETUTCDATE()
WHEN NOT MATCHED THEN INSERT (
    [FlowNodeCatalogId], [CatalogKey], [Name], [FlowNodeType], [Icon], [Category], [Color],
    [InputsJson], [OutputsJson], [ConfigSchemaJson], [DisplayOrder], [IsActive], [CreatedAt]
) VALUES (
    NEWID(), src.[CatalogKey], src.[Name], src.[FlowNodeType], src.[Icon], src.[Category], src.[Color],
    src.[InputsJson], src.[OutputsJson], src.[ConfigSchemaJson], src.[DisplayOrder], 1, GETUTCDATE()
);

GO
