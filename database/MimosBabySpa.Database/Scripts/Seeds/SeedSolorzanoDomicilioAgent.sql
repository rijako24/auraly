-- =============================================================================

-- SeedSolorzanoDomicilioAgent.sql

--

-- Agente inbound para contactos de domicilio de Vinos Artesanales Solorzano.

-- Atiende respuestas de domiciliarios a escalamientos externos y evita que

-- esos contactos caigan en el flujo comercial de Camila.

-- =============================================================================



SET NOCOUNT ON;



DECLARE @SolorzanoDeliveryBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @SolorzanoDeliveryAgentId    UNIQUEIDENTIFIER = 'D0EE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @SolorzanoDeliveryAgentTypeId UNIQUEIDENTIFIER;



IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @SolorzanoDeliveryBusinessId)

BEGIN

    PRINT N'SeedSolorzanoDomicilioAgent: negocio Solorzano no encontrado; omitiendo.';

    RETURN;

END



SELECT TOP (1) @SolorzanoDeliveryAgentTypeId = AgentTypeId

FROM dbo.Agents

WHERE BusinessId = @SolorzanoDeliveryBusinessId

ORDER BY CreatedAt;



IF @SolorzanoDeliveryAgentTypeId IS NULL

BEGIN

    SELECT TOP (1) @SolorzanoDeliveryAgentTypeId = AgentTypeId

    FROM dbo.AgentTypes

    WHERE IsActive = 1

    ORDER BY Name;

END



IF @SolorzanoDeliveryAgentTypeId IS NULL

BEGIN

    PRINT N'SeedSolorzanoDomicilioAgent: AgentType activo no encontrado; omitiendo.';

    RETURN;

END






DECLARE @SolorzanoDeliverySettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 12,
  "persona": "Eres el asistente de domicilios de Vinos Artesanales Solorzano. Atiendes solo a domiciliarios y coordinas si toman o rechazan pedidos asignados por WhatsApp.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\nResponde breve y operativo. Tu funcion es resolver pedidos de domicilio pendientes. Usa el codigo del pedido cuando este disponible.",
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
      "stages": [
        {
          "id": "order_request",
          "name": "Gestion de domicilio",
          "goal": "Resolver si el domiciliario acepta o rechaza un pedido pendiente.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Si el mensaje viene citado/respondiendo a una solicitud de domicilio, la cita identifica el pedido: si el contacto acepta/confirma/toma el pedido, acepta la solicitud; si rechaza o dice que no puede tomarlo, rechaza la solicitud. No pidas confirmacion ni motivo en esos casos. Busca el pedido solo cuando no haya cita ni payload interactivo, cuando necesites resolver por codigo PED/datos del pedido, o cuando haya varias ordenes pendientes; si hay ambiguedad, pide elegir mostrando request_code. Si el pedido esta vencido o no disponible, responde breve indicando que ya no puede gestionarse automaticamente. Tras aceptar agradece la confirmacion; tras rechazar indica que se registro el rechazo.",
          "collect": [],
          "signals": [
            {
              "type": "order_lookup",
              "description": "Consulta o referencia a una solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_accept",
              "description": "Aceptaci?n clara de la solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_reject",
              "description": "Rechazo claro de la solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "search_order",
              "operation": "internal.search_order",
              "trigger": "on_signal",
              "signal": "order_lookup",
              "arguments": {
                "query": "{{signal.order_lookup.value}}"
              },
              "onOutcome": {
                "internal.order_loaded": {}
              }
            },
            {
              "id": "accept_order",
              "operation": "internal.accept_order",
              "trigger": "on_signal",
              "signal": "order_accept",
              "arguments": {
                "response_text": "{{signal.order_accept.value}}"
              },
              "onOutcome": {
                "internal.order_accepted": {}
              }
            },
            {
              "id": "reject_order",
              "operation": "internal.reject_order",
              "trigger": "on_signal",
              "signal": "order_reject",
              "arguments": {
                "response_text": "{{signal.order_reject.value}}"
              },
              "onOutcome": {
                "internal.order_rejected": {}
              }
            }
          ]
        }
      ]
    }
  ],
  "factSchema": []
}';



IF ISJSON(@SolorzanoDeliverySettingsJson) <> 1

BEGIN

    THROW 51000, 'SeedSolorzanoDomicilioAgent: SettingsJson invalido.', 1;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @SolorzanoDeliveryAgentId)

BEGIN

    INSERT INTO dbo.Agents

        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,

         SettingsJson, Model, Temperature, CreatedAt)

    VALUES

        (@SolorzanoDeliveryAgentId, @SolorzanoDeliveryBusinessId, @SolorzanoDeliveryAgentTypeId, N'Domicilios Solorzano',

         N'Agente para aceptar o rechazar pedidos enviados a domiciliarios de Solorzano.',

         1, @SolorzanoDeliverySettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.Agents

    SET BusinessId = @SolorzanoDeliveryBusinessId,

        AgentTypeId = @SolorzanoDeliveryAgentTypeId,

        Name = N'Domicilios Solorzano',

        Description = N'Agente para aceptar o rechazar pedidos enviados a domiciliarios de Solorzano.',

        IsActive = 1,

        SettingsJson = @SolorzanoDeliverySettingsJson,

        Model = N'gpt-4.1-mini',

        Temperature = 0.2,

        UpdatedAt = GETUTCDATE()

    WHERE AgentId = @SolorzanoDeliveryAgentId;

END



PRINT N'SeedSolorzanoDomicilioAgent: agente de domicilios configurado para negocio ' + CAST(@SolorzanoDeliveryBusinessId AS NVARCHAR(36));
