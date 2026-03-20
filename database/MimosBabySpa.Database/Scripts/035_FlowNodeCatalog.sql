-- Seed / upsert catálogo de nodos para el editor de flujos (admin).
-- FlowNodeType debe coincidir con MimosBabySpa.Domain.Enums.FlowNodeType.
SET NOCOUNT ON;

MERGE INTO [dbo].[FlowNodeCatalog] AS tgt
USING (VALUES
    (N'start', N'Inicio', 0, N'circle-play', N'Control', N'#22c55e',
     N'[]',
     N'[{"id":"default","label":"Siguiente"}]',
     N'{"type":"object","properties":{},"additionalProperties":false}',
     0),
    (N'end', N'Fin', 7, N'circle-stop', N'Control', N'#ef4444',
     N'[{"id":"default","label":"Entrada"}]',
     N'[]',
     N'{"type":"object","properties":{},"additionalProperties":false}',
     1),
    (N'collect_fields', N'Recolectar campos', 1, N'list-checks', N'Entrada', N'#3b82f6',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"default","label":"Siguiente"}]',
     N'{"type":"object","properties":{"fields":{"type":"array","title":"Claves de variable","items":{"type":"string"},"description":"Usa \"*\" para todos los campos requeridos del flujo."},"instructions":{"type":"string","title":"Instrucciones adicionales"},"knowledgeSourceIds":{"type":"array","title":"IDs de fuentes (GUID)","items":{"type":"string"}}},"required":["fields"],"additionalProperties":true}',
     10),
    (N'action', N'Acción', 2, N'zap', N'Lógica', N'#eab308',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"success","label":"Éxito"},{"id":"failure","label":"Error"},{"id":"not_required","label":"No requerido (ej. generate_payment_link)"}]',
     N'{"type":"object","properties":{"action_type":{"type":"string","title":"Tipo de acción"},"input_mapping":{"type":"object","title":"Mapeo de entrada","additionalProperties":{"type":"string"}},"output_mapping":{"type":"object","title":"Mapeo de salida","additionalProperties":{"type":"string"}},"onSuccessTemplate":{"type":"string","title":"Plantilla si éxito"},"onFailureTemplate":{"type":"string","title":"Plantilla si error"},"additionalOutputPorts":{"type":"array","title":"Puertos extra (acciones personalizadas)","items":{"type":"string"},"x-dynamicOutputPort":true}},"required":["action_type"],"additionalProperties":true}',
     20),
    (N'llm_classify', N'Clasificar (LLM)', 3, N'brain', N'IA', N'#a855f7',
     N'[{"id":"default","label":"Entrada"}]',
     N'[]',
     N'{"type":"object","properties":{"prompt":{"type":"string","title":"Prompt"},"outputVariable":{"type":"string","title":"Variable de salida"},"possibleValues":{"type":"array","title":"Valores = IDs de puerto de salida","items":{"type":"string"},"x-dynamicOutputPort":true}},"required":["prompt","outputVariable","possibleValues"],"additionalProperties":true}',
     30),
    (N'intention_router', N'Router (clasificación)', 9, N'git-branch', N'Lógica', N'#f97316',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"default","label":"Por defecto"}]',
     N'{"type":"object","properties":{"classification":{"type":"object","title":"Clasificación LLM (fase 3)","properties":{"instructions":{"type":"string","title":"Instrucciones de clasificación"}}},"routes":{"type":"array","title":"Rutas","items":{"type":"object","properties":{"when":{"title":"Condición","anyOf":[{"type":"string","title":"Clave de intención"},{"type":"object","title":"Condición estructurada"}]},"port":{"type":"string","title":"Puerto","x-dynamicOutputPort":true}},"required":["when","port"]}},"defaultPort":{"type":"string","title":"Puerto por defecto","x-dynamicOutputPort":true}},"required":["routes","defaultPort"],"additionalProperties":true}',
     40),
    (N'generate_response', N'Generar respuesta', 4, N'message-square', N'Salida', N'#06b6d4',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"default","label":"Siguiente"}]',
     N'{"type":"object","properties":{"responseMode":{"type":"string","title":"Modo","enum":["llm","template"]},"instructions":{"type":"string","title":"Instrucciones o plantilla"},"waitForUser":{"type":"boolean","title":"Esperar respuesta del usuario"},"knowledgeSourceIds":{"type":"array","title":"IDs de fuentes (GUID)","items":{"type":"string"}}},"additionalProperties":true}',
     50),
    (N'wait_for_event', N'Esperar evento', 5, N'clock', N'Control', N'#64748b',
     N'[{"id":"default","label":"Entrada"}]',
     N'[{"id":"received","label":"Evento recibido"}]',
     N'{"type":"object","properties":{"event_type":{"type":"string","title":"Nombre del flag / evento"},"waitingMessage":{"type":"string","title":"Mensaje mientras espera"},"localIntentions":{"type":"array","title":"Intenciones locales","items":{"type":"object","properties":{"key":{"type":"string"},"behavior":{"type":"object","properties":{"action":{"type":"string"},"targetPort":{"type":"string","title":"Puerto destino","x-dynamicOutputPort":true}}}},"required":["key","behavior"]}}},"required":["event_type"],"additionalProperties":true}',
     60),
    (N'escalate', N'Escalar', 6, N'user-round', N'Salida', N'#dc2626',
     N'[{"id":"default","label":"Entrada"}]',
     N'[]',
     N'{"type":"object","properties":{"reason":{"type":"string","title":"Motivo"},"escalationMessage":{"type":"string","title":"Mensaje al usuario"},"contacts":{"type":"array","title":"Números WhatsApp","items":{"type":"string"}}},"additionalProperties":true}',
     70)
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
