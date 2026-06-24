-- =============================================================================
-- SeedSystemAgentTemplatesAndInboundContacts.sql
--
-- Templates del sistema y contactos inbound operativos por negocio.
-- Mantiene separados los agentes de delivery y operations.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @DeliveryTemplateId UNIQUEIDENTIFIER = 'A1111111-1111-1111-1111-111111111111';
DECLARE @OperationsTemplateId UNIQUEIDENTIFIER = 'A2222222-2222-2222-2222-222222222222';

DECLARE @DeliverySettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "maxToolIterations": 4,
  "historyWindowSize": 12,
  "persona": "Eres el asistente de domicilios del negocio. Atiendes solo a domiciliarios y coordinas si toman o rechazan solicitudes asignadas por WhatsApp.",
  "policies": "Responde breve y operativo. Tu funcion es resolver solicitudes de domicilio pendientes. No atiendas clientes finales ni solicitudes administrativas.",
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "delivery_assignment",
        "name": "Gestion de domicilio",
        "goal": "Resolver si el domiciliario acepta o rechaza una solicitud pendiente.",
        "hint": "En cada mensaje del contacto, primero llama resolve_external_interaction con message_text. Cuando la herramienta identifique varias interacciones pendientes, solicita cual quiere gestionar y muestra los attempt_code disponibles. Cuando el mensaje venga citado, traiga codigo, boton o la herramienta resuelva una sola interaccion, usa esa interaccion como referencia. Cuando requested_action sea accepted o el mensaje indique que el contacto toma o confirma la solicitud, llama complete_external_interaction con outcome_key=accepted. Cuando requested_action sea declined o el mensaje indique rechazo, llama complete_external_interaction con outcome_key=declined. Cuando la interaccion este identificada y la intencion del mensaje no sea clara, solicita una confirmacion breve mencionando attempt_code. Cuando no haya pendientes, informa que no tiene solicitudes pendientes. Despues de completar, responde usando los datos estructurados devueltos por la tool y el contexto del mensaje original.",
        "allowedTools": ["resolve_external_interaction", "complete_external_interaction"],
        "advanceWhenFacts": []
      }
    ]
  },
  "enabledTools": [
    "resolve_external_interaction",
    "complete_external_interaction"
  ],
  "guards": {},
  "notifications": {},
  "webhooks": {},
  "escalations": {
    "human": { "contacts": [], "killSwitchPhrases": [] },
    "external": { "enabled": false, "events": {} }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  }
}';

DECLARE @OperationsSettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "maxToolIterations": 6,
  "historyWindowSize": 12,
  "persona": "Eres el agente operativo interno del negocio. Atiendes solo contactos administrativos autorizados.",
  "policies": "Responde de forma breve y operativa. No atiendas solicitudes de clientes finales ni de domiciliarios. Todas las consultas y cambios deben usar las tools operativas, que trabajan siempre sobre el negocio actual. Para reagendar reservas por inconvenientes operativos, usa operations_request_reschedule para enviar el aviso al cliente y dejar que su respuesta siga por el flujo normal. No cambies fecha u hora desde operaciones.",
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "operations",
        "name": "Operacion interna",
        "goal": "Atender mensajes operativos autorizados del negocio: agenda, bloqueos, metricas, pedidos, ventas e historial de clientes.",
        "hint": "Usa operations_get_reservations para preguntas de agenda por dia o rango. Usa operations_block_availability para bloquear horarios o dias. Usa operations_get_business_metrics para ventas, pedidos, reservas y servicios mas vendidos. Usa operations_get_customer_history para ultima compra o historial de un cliente. Usa operations_request_reschedule para avisar a clientes afectados que deben reagendar; no muevas reservas directamente desde operaciones.",
        "allowedTools": [
          "operations_get_reservations",
          "operations_block_availability",
          "operations_request_reschedule",
          "operations_get_business_metrics",
          "operations_get_customer_history",
          "check_availability"
        ],
        "advanceWhenFacts": []
      }
    ]
  },
  "enabledTools": [
    "operations_get_reservations",
    "operations_block_availability",
    "operations_request_reschedule",
    "operations_get_business_metrics",
    "operations_get_customer_history",
    "check_availability"
  ],
  "guards": {},
  "notifications": {},
  "webhooks": {},
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Horarios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each slots}}\n- {{this}}\n{{/each}}\n\nCual prefieres?"
  },
  "escalations": {
    "human": { "contacts": [], "killSwitchPhrases": [] },
    "external": { "enabled": false, "events": {} }
  },
  "checkout": { "currency": "COP", "modes": {} }
}';

IF ISJSON(@DeliverySettingsJson) <> 1
    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Delivery SettingsJson invalido.', 1;

IF ISJSON(@OperationsSettingsJson) <> 1
    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Operations SettingsJson invalido.', 1;

MERGE dbo.AgentTemplates AS target
USING (VALUES
    (@DeliveryTemplateId, N'system.delivery', N'Agente de domicilios', N'delivery', N'Resuelve interacciones externas con domiciliarios.', @DeliverySettingsJson, N''),
    (@OperationsTemplateId, N'system.operations', N'Agente operativo', N'operations', N'Atiende contactos administrativos y operativos del negocio.', @OperationsSettingsJson, N'')
) AS source (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson, SystemPromptMarkdown)
ON target.[Key] = source.[Key]
WHEN MATCHED THEN
    UPDATE SET AgentTemplateId = source.AgentTemplateId,
               [Name] = source.[Name],
               Kind = source.Kind,
               [Description] = source.[Description],
               SettingsJson = source.SettingsJson,
               SystemPromptMarkdown = source.SystemPromptMarkdown,
               IsSystemTemplate = 1,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson, SystemPromptMarkdown, IsSystemTemplate, IsActive, CreatedAt)
    VALUES (source.AgentTemplateId, source.[Key], source.[Name], source.Kind, source.[Description], source.SettingsJson, source.SystemPromptMarkdown, 1, 1, GETUTCDATE());

DECLARE @AgentTypeId UNIQUEIDENTIFIER;
SELECT TOP (1) @AgentTypeId = AgentTypeId
FROM dbo.AgentTypes
WHERE IsActive = 1
ORDER BY Name;

IF @AgentTypeId IS NULL
BEGIN
    PRINT N'SeedSystemAgentTemplatesAndInboundContacts: AgentType activo no encontrado; omitiendo agentes inbound.';
    RETURN;
END

DECLARE @SolorzanoBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @SolorzanoDeliveryAgentId UNIQUEIDENTIFIER = 'D0EE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @SolorzanoOperationsAgentId UNIQUEIDENTIFIER = 'D1EE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @MimosOperationsAgentId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-2222222220A1';
DECLARE @LuisBusinessId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000001';
DECLARE @LuisOperationsAgentId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-0000000000A1';

IF EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @SolorzanoDeliveryAgentId)
BEGIN
    UPDATE dbo.Agents
    SET Kind = N'delivery',
        AgentTemplateId = @DeliveryTemplateId,
        SettingsJson = @DeliverySettingsJson,
        SystemPromptMarkdown = N'',
        Model = N'gpt-4.1-mini',
        Temperature = 0.2,
        MaxToolIterations = 4,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @SolorzanoDeliveryAgentId;
END

DECLARE @OpsAgents TABLE (BusinessId UNIQUEIDENTIFIER, AgentId UNIQUEIDENTIFIER, [Name] NVARCHAR(200), [Description] NVARCHAR(500));
INSERT INTO @OpsAgents (BusinessId, AgentId, [Name], [Description]) VALUES
    (@SolorzanoBusinessId, @SolorzanoOperationsAgentId, N'Operaciones Solorzano', N'Agente operativo interno de Vinos Artesanales Solorzano.'),
    (@MimosBusinessId, @MimosOperationsAgentId, N'Operaciones Mimos', N'Agente operativo interno de Mimos Baby Spa.'),
    (@LuisBusinessId, @LuisOperationsAgentId, N'Operaciones Luis Petit', N'Agente operativo interno de Luis Petit Barber.');

MERGE dbo.Agents AS target
USING (
    SELECT BusinessId, AgentId, [Name], [Description]
    FROM @OpsAgents oa
    WHERE EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId = oa.BusinessId)
) AS source
ON target.AgentId = source.AgentId
WHEN MATCHED THEN
    UPDATE SET BusinessId = source.BusinessId,
               AgentTypeId = @AgentTypeId,
               AgentTemplateId = @OperationsTemplateId,
               [Name] = source.[Name],
               [Description] = source.[Description],
               Kind = N'operations',
               IsActive = 1,
               SettingsJson = @OperationsSettingsJson,
               SystemPromptMarkdown = N'',
               Model = N'gpt-4.1-mini',
               Temperature = 0.2,
               MaxToolIterations = 6,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (AgentId, BusinessId, AgentTypeId, AgentTemplateId, [Name], [Description], Kind, IsActive,
            SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES (source.AgentId, source.BusinessId, @AgentTypeId, @OperationsTemplateId, source.[Name], source.[Description], N'operations', 1,
            @OperationsSettingsJson, N'', N'gpt-4.1-mini', 0.2, 6, GETUTCDATE());

DECLARE @Contacts TABLE (
    BusinessInboundContactId UNIQUEIDENTIFIER,
    BusinessId UNIQUEIDENTIFIER,
    [Type] NVARCHAR(50),
    [Key] NVARCHAR(100),
    [Name] NVARCHAR(200),
    [Role] NVARCHAR(100),
    PhoneNumber NVARCHAR(50),
    PhoneNormalized NVARCHAR(50),
    InboundAgentId UNIQUEIDENTIFIER,
    CapabilitiesJson NVARCHAR(MAX)
);

INSERT INTO @Contacts VALUES
    ('E0EE3BA9-E6BF-43E2-8C1A-560CB724688B', @SolorzanoBusinessId, N'delivery', N'domicilio_solorzano', N'Domicilio Solorzano', N'delivery', N'+573042052007', N'573042052007', @SolorzanoDeliveryAgentId, N'{"scope":"delivery"}'),
    ('E1EE3BA9-E6BF-43E2-8C1A-560CB724688B', @SolorzanoBusinessId, N'operations', N'operaciones_solorzano', N'Operaciones Solorzano', N'operations', N'+573004442469', N'573004442469', @SolorzanoOperationsAgentId, N'{"scope":"operations"}'),
    ('22222222-2222-2222-2222-2222222220B1', @MimosBusinessId, N'operations', N'operaciones_mimos', N'Operaciones Mimos', N'operations', N'+573012926660', N'573012926660', @MimosOperationsAgentId, N'{"scope":"operations"}'),
    ('BABA0000-0000-0000-0000-0000000000B1', @LuisBusinessId, N'operations', N'operaciones_luis_petit', N'Operaciones Luis Petit', N'operations', N'+573042052007', N'573042052007', @LuisOperationsAgentId, N'{"scope":"operations"}');

MERGE dbo.BusinessInboundContacts AS target
USING (
    SELECT *
    FROM @Contacts c
    WHERE EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId = c.BusinessId)
      AND EXISTS (SELECT 1 FROM dbo.Agents a WHERE a.AgentId = c.InboundAgentId AND a.BusinessId = c.BusinessId)
) AS source
ON target.BusinessId = source.BusinessId AND target.PhoneNormalized = source.PhoneNormalized
WHEN MATCHED THEN
    UPDATE SET BusinessInboundContactId = source.BusinessInboundContactId,
               [Type] = source.[Type],
               [Key] = source.[Key],
               [Name] = source.[Name],
               [Role] = source.[Role],
               PhoneNumber = source.PhoneNumber,
               InboundAgentId = source.InboundAgentId,
               CapabilitiesJson = source.CapabilitiesJson,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (BusinessInboundContactId, BusinessId, [Type], [Key], [Name], [Role], PhoneNumber, PhoneNormalized,
            InboundAgentId, CapabilitiesJson, IsActive, CreatedAt)
    VALUES (source.BusinessInboundContactId, source.BusinessId, source.[Type], source.[Key], source.[Name], source.[Role], source.PhoneNumber, source.PhoneNormalized,
            source.InboundAgentId, source.CapabilitiesJson, 1, GETUTCDATE());

PRINT N'SeedSystemAgentTemplatesAndInboundContacts: templates y contactos inbound configurados.';








