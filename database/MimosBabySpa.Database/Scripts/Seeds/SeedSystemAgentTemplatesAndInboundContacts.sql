-- =============================================================================

-- SeedSystemAgentTemplatesAndInboundContacts.sql

--

-- Templates del sistema y contactos inbound operativos por negocio.

-- Mantiene separados los agentes de domicilio y operations.

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
  "enabledTools": [
    "search_order",
    "accept_order_request",
    "reject_order_request"
  ],
  "guards": {},
  "notifications": {},
  "webhooks": {},
  "escalations": {
    "human": {
      "contacts": []
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  },
  "flows": [
    {
      "id": "order_request",
      "type": "primary",
      "routingGuidance": "Use this primary flow for external order request interactions with delivery contacts.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "order_request",
          "name": "Gestion de domicilio",
          "goal": "Resolver si el domiciliario acepta o rechaza una solicitud pendiente.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Si el mensaje viene citado/respondiendo a una solicitud de domicilio, la cita identifica el pedido: si el contacto acepta/confirma/toma el pedido, acepta la solicitud; si rechaza o dice que no puede tomarlo, rechaza la solicitud. No pidas confirmacion ni motivo en esos casos. Busca el pedido solo cuando no haya cita ni payload interactivo, cuando necesites resolver por codigo PED/datos del pedido, o cuando haya varias ordenes pendientes; si hay ambiguedad, pide elegir mostrando request_code. Si el pedido esta vencido o no disponible, responde breve indicando que ya no puede gestionarse automaticamente. Tras aceptar agradece la confirmacion; tras rechazar indica que se registro el rechazo.",
          "allowedActions": [
            "search_order",
            "accept_order_request",
            "reject_order_request"
          ],
          "collect": []
        }
      ]
    }
  ]
}';



DECLARE @OperationsSettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "maxToolIterations": 6,
  "historyWindowSize": 12,
  "persona": "Eres el agente operativo interno del negocio. Atiendes solo contactos administrativos autorizados.",
  "policies": "Responde de forma breve y operativa. No atiendas solicitudes de clientes finales ni de domiciliarios. Todas las consultas y cambios deben usar las tools operativas, que trabajan siempre sobre el negocio actual. Para reagendar reservas por inconvenientes operativos, usa operations_request_reschedule para enviar el aviso al cliente y dejar que su respuesta siga por el flujo normal. No cambies fecha u hora desde operaciones.",
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
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "escalations": {
    "human": {
      "contacts": []
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  },
  "flows": [
    {
      "id": "order_request",
      "type": "primary",
      "routingGuidance": "Use this primary flow for external order request interactions with delivery contacts.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "operations",
          "name": "Operacion interna",
          "goal": "Atender mensajes operativos autorizados del negocio: agenda, bloqueos, metricas, pedidos, ventas e historial de clientes.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Consulta reservas operativas para preguntas de agenda por dia o rango. Bloquea disponibilidad para bloquear horarios o dias. Consulta metricas de negocio para ventas, pedidos, reservas y servicios mas vendidos. Consulta historial de cliente para ultima compra o historial de un cliente. Solicita reagenda operativa para avisar a clientes afectados que deben reagendar; no muevas reservas directamente desde operaciones.",
          "allowedActions": [
            "operaciones_consultar_reservas",
            "operaciones_bloquear_disponibilidad",
            "operaciones_solicitar_reagenda",
            "operaciones_metricas_negocio",
            "operaciones_historial_cliente",
            "ejecutar_check_availability"
          ],
          "collect": []
        }
      ]
    }
  ]
}';



IF ISJSON(@DeliverySettingsJson) <> 1

    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Delivery SettingsJson invalido.', 1;



IF ISJSON(@OperationsSettingsJson) <> 1

    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Operations SettingsJson invalido.', 1;



MERGE dbo.AgentTemplates AS target

USING (VALUES

    (@DeliveryTemplateId, N'system.domicilio', N'Agente de domicilios', N'domicilio', N'Resuelve interacciones externas con domiciliarios.', @DeliverySettingsJson, N''),

    (@OperationsTemplateId, N'system.operations', N'Agente operativo', N'operations', N'Atiende contactos administrativos y operativos del negocio.', @OperationsSettingsJson, N'')

) AS source (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson, SystemPromptMarkdown)

ON target.AgentTemplateId = source.AgentTemplateId

   OR target.[Key] = source.[Key]

WHEN MATCHED THEN

    UPDATE SET [Key] = source.[Key],

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

    SET Kind = N'domicilio',

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

    ('E2EE3BA9-E6BF-43E2-8C1A-560CB724688B', @SolorzanoBusinessId, N'domicilio', N'supervoy', N'SuperVoy', N'domicilio', N'+573023823535', N'573023823535', @SolorzanoDeliveryAgentId, N'{"scope":"domicilio"}'),

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



DELETE FROM dbo.BusinessInboundContacts

WHERE BusinessId = @SolorzanoBusinessId

  AND BusinessInboundContactId = 'E0EE3BA9-E6BF-43E2-8C1A-560CB724688B'

  AND [Type] = N'domicilio'

  AND PhoneNormalized = N'573042052007';



DELETE FROM dbo.BusinessInboundContacts

WHERE BusinessId = @SolorzanoBusinessId

  AND BusinessInboundContactId = 'E3EE3BA9-E6BF-43E2-8C1A-560CB724688B'

  AND [Type] = N'domicilio'

  AND PhoneNormalized = N'573006704013';



PRINT N'SeedSystemAgentTemplatesAndInboundContacts: templates y contactos inbound configurados.';

