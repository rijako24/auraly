-- =============================================================================
-- SeedSolorzanoDeliveryAgent.sql
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
    PRINT N'SeedSolorzanoDeliveryAgent: negocio Solorzano no encontrado; omitiendo.';
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
    PRINT N'SeedSolorzanoDeliveryAgent: AgentType activo no encontrado; omitiendo.';
    RETURN;
END

DECLARE @SolorzanoDeliverySystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SolorzanoDeliverySettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "maxToolIterations": 4,
  "historyWindowSize": 12,
  "persona": "Eres el asistente de domicilios de Vinos Artesanales Solorzano. Atiendes solo a domiciliarios y coordinas si toman o rechazan pedidos asignados por WhatsApp.",
  "policies": "Responde breve y operativo. Tu funcion es resolver pedidos de domicilio pendientes. Usa el codigo del pedido cuando este disponible.",
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "delivery_assignment",
        "name": "Gestion de domicilio",
        "goal": "Resolver si el domiciliario acepta o rechaza un pedido pendiente.",
        "hint": "En cada mensaje del domiciliario, primero llama resolve_external_escalation con message_text. Si la herramienta devuelve requested_action=accept y external_escalation_id, llama accept_external_escalation. Si devuelve requested_action=decline y external_escalation_id, llama decline_external_escalation. Si hay un pedido resuelto pero no hay accion clara, responde con una pregunta corta para que confirme si lo toma o no, mencionando el attempt_code. Si no hay pedidos pendientes, responde que no tiene domicilios pendientes.",
        "allowedTools": ["resolve_external_escalation", "accept_external_escalation", "decline_external_escalation"],
        "advanceWhenFacts": []
      }
    ]
  },
  "enabledTools": [
    "resolve_external_escalation",
    "accept_external_escalation",
    "decline_external_escalation"
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

IF ISJSON(@SolorzanoDeliverySettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedSolorzanoDeliveryAgent: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @SolorzanoDeliveryAgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@SolorzanoDeliveryAgentId, @SolorzanoDeliveryBusinessId, @SolorzanoDeliveryAgentTypeId, N'Domicilios Solorzano',
         N'Agente para aceptar o rechazar pedidos enviados a domiciliarios de Solorzano.',
         1, @SolorzanoDeliverySettingsJson, @SolorzanoDeliverySystemPrompt, N'gpt-4.1-mini', 0.2, 4, GETUTCDATE());
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
        SystemPromptMarkdown = @SolorzanoDeliverySystemPrompt,
        Model = N'gpt-4.1-mini',
        Temperature = 0.2,
        MaxToolIterations = 4,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @SolorzanoDeliveryAgentId;
END

PRINT N'SeedSolorzanoDeliveryAgent: agente de domicilios configurado para negocio ' + CAST(@SolorzanoDeliveryBusinessId AS NVARCHAR(36));
