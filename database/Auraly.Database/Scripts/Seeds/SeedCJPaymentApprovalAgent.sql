-- =============================================================================
-- Agente interno y exclusivo para aprobacion manual de pagos de CJ.
-- El telefono autorizado se configura en BusinessInboundContacts con
-- Type o Key = payment_approver y se enruta a @PaymentAgentId.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @TemplateId UNIQUEIDENTIFIER = 'A3333333-3333-3333-3333-333333333333';
DECLARE @PaymentAgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000021';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;

SELECT TOP (1) @AgentTypeId = AgentTypeId
FROM dbo.AgentTypes
WHERE IsActive = 1
ORDER BY Name;

IF @AgentTypeId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    PRINT N'SeedCJPaymentApprovalAgent: negocio CJ o AgentType activo no encontrado; omitiendo.';
    RETURN;
END

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.1,
  "historyWindowSize": 12,
  "persona": "Eres el aprobador interno de pagos de CJ Distribuciones. Atiendes exclusivamente al contacto autorizado para revisar y confirmar pagos manuales pendientes.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde con tono amable, breve, claro y profesional.\n- Tu unica funcion es consultar y confirmar pagos manuales pendientes de CJ Distribuciones.\n- Si te hablan de cualquier otro tema, explica cortesmente que solo puedes ayudar a validar pagos.\n- Nunca confirmes un pago ambiguo ni elijas entre varios pagos por proximidad, orden de mensajes o suposicion.\n- Cuando una solicitud de confirmacion no identifica el pedido, muestra los pagos pendientes; si solo hay uno, presenta sus datos y pide confirmacion explicita antes de aprobarlo.\n- Cuando hay varios pagos pendientes, muestra sus codigos y pide elegir uno.\n- Un boton de confirmar ya identifica el pago exacto y se procesa directamente.",
  "notifications": {},
  "webhooks": {},
  "escalations": {
    "human": { "contacts": [] },
    "external": { "enabled": false, "events": {} }
  },
  "checkout": { "currency": "COP", "modes": {} },
  "templates": {
    "manual_payment_single": "Encontre este pago manual pendiente:\n\n- Codigo: *{{#each payments}}{{payment_code}}{{/each}}*\n{{#each payments}}- Pedido: {{order_number}}\n- Cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n- Entrega: {{delivery_address}}\n- Total: ${{amount}} {{currency}}\n{{/each}}\nConfirmas que debo aprobar este pago?",
    "manual_payment_multiple": "Hay varios pagos manuales pendientes:\n\n{{#each payments}}- *{{payment_code}}* | Pedido {{order_number}} | {{customer_name}} | ${{amount}} {{currency}}\n{{/each}}\nIndica el codigo o el pedido exacto que deseas confirmar.",
    "manual_payment_none": "No encontre pagos manuales pendientes con esos datos."
  },
  "interactiveActions": {
    "manual_payment": {
      "confirm": {
        "operation": "internal.confirm_manual_payment",
        "arguments": {
          "payment_transaction_id": "{source_id}"
        }
      }
    }
  },
  "flows": [
    {
      "id": "payment_approval",
      "type": "primary",
      "routingGuidance": "Use this flow only for internal manual-payment lookup and approval.",
      "stages": [
        {
          "id": "payment_approval",
          "name": "Aprobacion de pagos",
          "goal": "Consultar pagos manuales pendientes y confirmar unicamente el pago identificado sin ambiguedad.",
          "advanceWhenFacts": [],
          "collect": [],
          "conversationGuidance": "Si preguntan por pagos pendientes o piden confirmar sin identificar uno, consulta los pagos y presenta el resultado. Si piden confirmar e incluyen codigo de pago, numero de pedido, cliente o telefono que identifica uno solo, confirma usando esa referencia. Si acabas de mostrar un unico pago y la persona responde afirmativamente, confirma el id seleccionado. Para cualquier otra intencion responde amablemente que tu unica funcion es validar pagos.",
          "signals": [
            {
              "type": "manual_payment_lookup",
              "description": "Consulta de pagos manuales pendientes o solicitud de confirmar un pago sin identificar cual. El valor conserva cualquier dato de pedido, cliente o telefono mencionado; usa una cadena vacia si no hay ninguno.",
              "valueSchema": { "type": "string" }
            },
            {
              "type": "manual_payment_confirm_identified",
              "description": "Orden explicita de confirmar un pago que incluye codigo, pedido, cliente o telefono suficiente para identificarlo. El valor contiene literalmente esa referencia identificadora.",
              "valueSchema": { "type": "string" }
            },
            {
              "type": "manual_payment_confirm_selected",
              "description": "Confirmacion afirmativa explicita del unico pago que el agente mostro inmediatamente antes. Solo aplica cuando selected_payment_transaction_id ya existe.",
              "valueSchema": { "type": "string" }
            }
          ],
          "actions": [
            {
              "id": "search_manual_payments",
              "operation": "internal.search_manual_payments",
              "trigger": "on_signal",
              "signal": "manual_payment_lookup",
              "arguments": { "query": "{{signal.manual_payment_lookup.value}}" },
              "execution": { "idempotency": "none" },
              "onOutcome": {
                "payment.single_pending": {
                  "effects": [
                    { "type": "facts.set_from_outcome", "bindings": { "selected_payment_transaction_id": "selected_payment_transaction_id" } },
                    { "type": "presentation.add", "template": "manual_payment_single", "mode": "Exclusive", "priority": "Required" }
                  ],
                  "response": { "suppressText": true }
                },
                "payment.multiple_pending": {
                  "effects": [
                    { "type": "facts.clear", "facts": ["selected_payment_transaction_id"] },
                    { "type": "presentation.add", "template": "manual_payment_multiple", "mode": "Exclusive", "priority": "Required" }
                  ],
                  "response": { "suppressText": true }
                },
                "payment.none_pending": {
                  "effects": [
                    { "type": "facts.clear", "facts": ["selected_payment_transaction_id"] },
                    { "type": "presentation.add", "template": "manual_payment_none", "mode": "Exclusive", "priority": "Required" }
                  ],
                  "response": { "suppressText": true }
                }
              }
            },
            {
              "id": "confirm_identified_manual_payment",
              "operation": "internal.confirm_manual_payment",
              "trigger": "on_signal",
              "signal": "manual_payment_confirm_identified",
              "arguments": { "query": "{{signal.manual_payment_confirm_identified.value}}" },
              "execution": { "idempotency": "none" },
              "onOutcome": {
                "payment.confirmed": { "effects": [{ "type": "facts.clear", "facts": ["selected_payment_transaction_id"] }], "response": { "guidance": "Confirma brevemente que el pago exacto fue aprobado y procesado." } },
                "payment.already_confirmed": { "response": { "guidance": "Indica brevemente que ese pago ya estaba confirmado." } },
                "payment.ambiguous": { "response": { "mode": "ask_clarification", "guidance": "Indica que coinciden varios pagos y pide codigo o pedido exacto." } },
                "payment.not_found": { "response": { "mode": "ask_clarification", "guidance": "Indica que no encontraste un pago pendiente con esa referencia y pide verificarla." } }
              }
            },
            {
              "id": "confirm_selected_manual_payment",
              "operation": "internal.confirm_manual_payment",
              "trigger": "on_signal",
              "signal": "manual_payment_confirm_selected",
              "condition": { "factPresent": "selected_payment_transaction_id" },
              "arguments": { "payment_transaction_id": "{{fact.selected_payment_transaction_id}}" },
              "execution": { "idempotency": "none" },
              "onOutcome": {
                "payment.confirmed": { "effects": [{ "type": "facts.clear", "facts": ["selected_payment_transaction_id"] }], "response": { "guidance": "Confirma brevemente que el pago fue aprobado y procesado." } },
                "payment.already_confirmed": { "effects": [{ "type": "facts.clear", "facts": ["selected_payment_transaction_id"] }], "response": { "guidance": "Indica brevemente que ese pago ya estaba confirmado." } }
              }
            }
          ],
          "response": {
            "guidance": "Si el mensaje no corresponde a consultar o confirmar pagos manuales, responde de forma muy amable y breve que tu unica funcion es validar pagos de CJ Distribuciones."
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "selected_payment_transaction_id",
      "role": "payment.selected_transaction_id",
      "label": "pago pendiente seleccionado",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    }
  ]
}';

IF ISJSON(@SettingsJson) <> 1
    THROW 51000, 'SeedCJPaymentApprovalAgent: SettingsJson invalido.', 1;

MERGE dbo.AgentTemplates AS target
USING (SELECT @TemplateId AS AgentTemplateId) AS source
ON target.AgentTemplateId = source.AgentTemplateId OR target.[Key] = N'system.payment_approval'
WHEN MATCHED THEN
    UPDATE SET [Key] = N'system.payment_approval',
               [Name] = N'Aprobador de pagos',
               Kind = N'payment_approval',
               [Description] = N'Consulta y confirma exclusivamente pagos manuales pendientes.',
               SettingsJson = @SettingsJson,
               IsSystemTemplate = 1,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson, IsSystemTemplate, IsActive, CreatedAt)
    VALUES (@TemplateId, N'system.payment_approval', N'Aprobador de pagos', N'payment_approval',
            N'Consulta y confirma exclusivamente pagos manuales pendientes.', @SettingsJson, 1, 1, GETUTCDATE());

MERGE dbo.Agents AS target
USING (SELECT @PaymentAgentId AS AgentId) AS source
ON target.AgentId = source.AgentId
WHEN MATCHED THEN
    UPDATE SET BusinessId = @BusinessId,
               AgentTypeId = @AgentTypeId,
               AgentTemplateId = @TemplateId,
               [Name] = N'Aprobador de pagos CJ',
               [Description] = N'Agente interno exclusivo para validar pagos manuales de CJ Distribuciones.',
               Kind = N'payment_approval',
               IsActive = 1,
               SettingsJson = @SettingsJson,
               Model = N'gpt-4.1-mini',
               Temperature = 0.1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (AgentId, BusinessId, AgentTypeId, AgentTemplateId, [Name], [Description], Kind, IsActive,
            SettingsJson, Model, Temperature, CreatedAt)
    VALUES (@PaymentAgentId, @BusinessId, @AgentTypeId, @TemplateId, N'Aprobador de pagos CJ',
            N'Agente interno exclusivo para validar pagos manuales de CJ Distribuciones.', N'payment_approval', 1,
            @SettingsJson, N'gpt-4.1-mini', 0.1, GETUTCDATE());

-- Conserva el telefono configurado por ambiente y solo normaliza su enrutamiento.
UPDATE dbo.BusinessInboundContacts
SET InboundAgentId = @PaymentAgentId,
    [Type] = N'payment_approver',
    [Role] = N'payment_approval',
    IsActive = 1,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ([Type] = N'payment_approver' OR [Key] = N'payment_approver');

PRINT N'SeedCJPaymentApprovalAgent: aprobador de pagos CJ configurado; asigne un BusinessInboundContact payment_approver al numero autorizado.';
