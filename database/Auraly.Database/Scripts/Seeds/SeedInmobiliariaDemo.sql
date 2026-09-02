-- Demo idempotente: una inmobiliaria que califica por avance del journey,
-- presupuesto/financiación y disposición a visitar, sin asumir que todos los
-- negocios usan los mismos criterios.
SET NOCOUNT ON;

IF LOWER(N'$(DeploymentEnvironment)') = N'prod'
BEGIN
    PRINT N'SeedInmobiliariaDemo: seed de demostración omitido en producción.';
    RETURN;
END;

DECLARE @TenantId UNIQUEIDENTIFIER = '8D91B781-6B75-4FD2-903F-2FD2D537D001';
DECLARE @BusinessId UNIQUEIDENTIFIER = '8D91B781-6B75-4FD2-903F-2FD2D537D002';
DECLARE @AgentId UNIQUEIDENTIFIER = '8D91B781-6B75-4FD2-903F-2FD2D537D003';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;
DECLARE @SubscriptionId UNIQUEIDENTIFIER = '8D91B781-6B75-4FD2-903F-2FD2D537D004';
DECLARE @PlanId UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'Inmobiliaria Horizonte Demo', N'demo@horizonte.test', 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
    INSERT INTO dbo.Businesses (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
    VALUES (@BusinessId, @TenantId, N'Inmobiliaria Horizonte Demo', N'Negocio de prueba para calificación de leads inmobiliarios.', N'Bogotá, Colombia', N'+573001234567', N'demo@horizonte.test', N'https://horizonte.test', 1, SYSUTCDATETIME());

SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Asesor inmobiliario';
IF @AgentTypeId IS NULL
BEGIN
    SET @AgentTypeId = '8D91B781-6B75-4FD2-903F-2FD2D537D005';
    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)
    VALUES (@AgentTypeId, N'Asesor inmobiliario', N'Agente de descubrimiento y calificación inmobiliaria.', 1);
END;

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 20,
  "extractorHistoryWindowSize": 2,
  "persona": "Eres Nora, asesora digital de Inmobiliaria Horizonte. Atiendes en español colombiano, con tono cercano y profesional. Tu objetivo es entender lo que busca la persona, orientarla sin inventar disponibilidad ni precios, y entregarla a un asesor humano cuando haya señales suficientes para una visita o asesoría.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\\n\\nNo inventes inmuebles, precios, crédito ni disponibilidad. Pregunta solo lo que falte para orientar el siguiente paso. No trates el presupuesto como requisito universal: úsalo para ajustar opciones cuando la persona pueda compartirlo. La calificación es interna; nunca menciones puntajes ni bandas al cliente.",
  "conversationOpening": { "enabled": true, "allowQuestions": true, "guidance": "Saluda brevemente y pregunta qué tipo de inmueble busca y en qué zona." },
  "failureResponses": { "llmUnavailable": "Estoy teniendo un inconveniente temporal. Déjame tus datos y un asesor te contactará." },
  "conversationFollowUp": { "enabled": true, "delayMinutes": 240, "respectOperatingHours": true, "guidance": "Retoma con una sola pregunta concreta sobre el dato que falta para orientar la búsqueda o coordinar una visita." },
  "factSchema": [
    { "key": "property_type", "role": "real_estate.property_type", "label": "tipo de inmueble", "type": "string", "source": "user", "scope": "request", "extractionGuidance": "Tipo de inmueble buscado, por ejemplo apartamento, casa, local, lote u oficina." },
    { "key": "target_zone", "role": "real_estate.target_zone", "label": "zona de interés", "type": "string", "source": "user", "scope": "request", "extractionGuidance": "Barrio, ciudad o zona donde desea buscar." },
    { "key": "purchase_timeline", "role": "real_estate.purchase_timeline", "label": "plazo de compra o arriendo", "type": "string", "source": "user", "scope": "request", "options": [{ "value": "immediate", "label": "Inmediato" }, { "value": "one_to_three_months", "label": "1 a 3 meses" }, { "value": "exploring", "label": "Solo explorando" }] },
    { "key": "budget_range", "role": "real_estate.budget_range", "label": "rango de presupuesto", "type": "string", "source": "user", "scope": "request", "extractionGuidance": "Rango aproximado cuando el cliente lo comparte; no inventarlo ni presionarlo." },
    { "key": "financing_status", "role": "real_estate.financing_status", "label": "estado de financiación", "type": "string", "source": "user", "scope": "request", "options": [{ "value": "cash", "label": "Recursos propios" }, { "value": "preapproved", "label": "Crédito preaprobado" }, { "value": "exploring_credit", "label": "Explorando crédito" }, { "value": "unknown", "label": "Aún no definido" }] },
    { "key": "visit_interest", "role": "real_estate.visit_interest", "label": "interés de visita", "type": "string", "source": "user", "scope": "request", "options": [{ "value": "yes", "label": "Quiere visita o asesoría" }, { "value": "later", "label": "Quiere continuar después" }] },
    { "key": "customer_name", "role": "customer.name", "label": "nombre", "type": "string", "source": "user", "scope": "customer" },
    { "key": "customer_email", "role": "customer.email", "label": "correo", "type": "email", "source": "user", "scope": "customer" },
    { "key": "contact_consent", "role": "customer.contact_consent", "label": "autorización de contacto", "type": "string", "source": "user", "scope": "customer", "options": [{ "value": "yes", "label": "Sí" }, { "value": "no", "label": "No" }] }
  ],
  "flows": [{
    "id": "property_lead",
    "type": "primary",
    "routingGuidance": "Descubrimiento y calificación de una persona que busca comprar o arrendar un inmueble.",
    "stages": [
      {
        "id": "discovery",
        "name": "Búsqueda inicial",
        "goal": "Comprender qué inmueble busca, en qué zona y para cuándo.",
        "collect": ["property_type", "target_zone", "purchase_timeline"],
        "advanceWhenFacts": ["property_type", "target_zone", "purchase_timeline"],
        "awaitCustomerReply": true,
        "conversationGuidance": "Responde primero las dudas generales y reúne en una sola pregunta los datos faltantes: tipo de inmueble, zona y plazo. No preguntes presupuesto todavía si no aporta a la conversación.",
        "leadQualification": { "band": "exploring", "priority": 15, "label": "Búsqueda inicial" }
      },
      {
        "id": "fit",
        "name": "Perfil de búsqueda",
        "goal": "Conocer presupuesto aproximado y situación de financiación cuando corresponda.",
        "collect": ["budget_range", "financing_status"],
        "advanceWhenFacts": ["financing_status"],
        "awaitCustomerReply": true,
        "conversationGuidance": "Pregunta por financiación y, si la persona se siente cómoda, por un rango aproximado. Explica que sirve para orientar opciones; acepta que aún no tenga presupuesto definido.",
        "leadQualification": { "band": "interested", "priority": 45, "label": "Perfil de búsqueda definido" }
      },
      {
        "id": "visit_intent",
        "name": "Interés de visita",
        "goal": "Confirmar si desea coordinar una visita o una asesoría con un agente.",
        "collect": ["visit_interest", "customer_name", "customer_email", "contact_consent"],
        "advanceWhenFacts": ["visit_interest", "customer_name", "contact_consent"],
        "awaitCustomerReply": true,
        "conversationGuidance": "Propón una visita o asesoría solo después de entender la búsqueda. Si acepta, pide nombre y autorización para que un asesor la contacte. El correo es útil pero opcional.",
        "leadQualification": { "band": "high_intent", "priority": 80, "label": "Quiere visita o asesoría" }
      },
      {
        "id": "sales_handoff",
        "name": "Entrega a asesor",
        "goal": "Entregar el contexto completo a un asesor humano sin prometer disponibilidad.",
        "actions": [{
          "id": "handoff_qualified_property_lead",
          "operation": "escalation.request_human",
          "trigger": "on_enter",
          "condition": { "factEquals": { "key": "contact_consent", "value": "yes" } },
          "arguments": { "reason": "qualified_property_lead", "last_user_message": "{{turn.message}}" },
          "execution": { "idempotency": "once_per_request", "maxAttempts": 1 },
          "onOutcome": {
            "escalation.requested": { "response": { "guidance": "Confirma que un asesor recibirá el contexto y se pondrá en contacto para continuar, sin prometer un inmueble ni una hora específica." } },
            "escalation.notification_failed": { "response": { "guidance": "Informa que registraste la solicitud y que el equipo dará continuidad apenas sea posible." } }
          }
        }],
        "leadQualification": { "band": "sales_ready", "priority": 100, "label": "Lista para atención comercial" }
      }
    ]
  }],
  "globalActions": [],
  "templates": {},
  "messageSequences": {},
  "notifications": {},
  "escalations": { "human": { "contacts": [] } },
  "checkout": { "currency": "COP", "modes": {} },
  "commerce": { "enabled": false },
  "operatingHours": { "enforce": false }
}';

IF ISJSON(@SettingsJson) <> 1
    THROW 51000, 'SeedInmobiliariaDemo: SettingsJson inválido.', 1;

IF EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
    UPDATE dbo.Agents SET Name = N'Nora - Inmobiliaria Horizonte', Description = N'Asesora inmobiliaria de prueba con calificación por etapas.', Kind = N'customer', IsActive = 1, SettingsJson = @SettingsJson, Model = N'gpt-4.1-mini', Temperature = 0.2, UpdatedAt = SYSUTCDATETIME() WHERE AgentId = @AgentId;
ELSE
    INSERT INTO dbo.Agents (AgentId, BusinessId, AgentTypeId, Name, Description, Kind, IsActive, SettingsJson, Model, Temperature, CreatedAt)
    VALUES (@AgentId, @BusinessId, @AgentTypeId, N'Nora - Inmobiliaria Horizonte', N'Asesora inmobiliaria de prueba con calificación por etapas.', N'customer', 1, @SettingsJson, N'gpt-4.1-mini', 0.2, SYSUTCDATETIME());

SELECT TOP 1 @PlanId = SubscriptionPlanId FROM dbo.SubscriptionPlans WHERE IsActive = 1 ORDER BY MonthlyPriceCop, CreatedAt;
IF @PlanId IS NULL
    THROW 51000, 'SeedInmobiliariaDemo: no existe un plan activo para la suscripción de prueba.', 1;

MERGE dbo.BusinessSubscriptions AS target
USING (SELECT @SubscriptionId AS BusinessSubscriptionId, @BusinessId AS BusinessId, @PlanId AS SubscriptionPlanId) AS source
ON target.BusinessSubscriptionId = source.BusinessSubscriptionId
WHEN MATCHED THEN UPDATE SET SubscriptionPlanId = source.SubscriptionPlanId, Status = 1, CurrentPeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1), CurrentPeriodEnd = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)), PlanCodeSnapshot = N'DEMO', PlanNameSnapshot = N'Demo inmobiliaria', MonthlyPriceCop = 0, IncludedCredits = 1000, MaxVariableCostCop = 0, MaxVariableCostPercent = 0, ExtraCredits = 0, ExtraVariableCostCop = 0, AutoRenew = 0, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (BusinessSubscriptionId, BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd, PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits, MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop, AutoRenew, CreatedAt, UpdatedAt)
VALUES (@SubscriptionId, @BusinessId, @PlanId, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1), DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)), N'DEMO', N'Demo inmobiliaria', 0, 1000, 0, 0, 0, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());

UPDATE dbo.BusinessSubscriptions SET Status = 4, UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @BusinessId AND BusinessSubscriptionId <> @SubscriptionId AND Status IN (1, 2, 3);

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessUsagePeriods WHERE BusinessSubscriptionId = @SubscriptionId AND PeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1))
    INSERT INTO dbo.BusinessUsagePeriods (BusinessSubscriptionId, BusinessId, PeriodStart, PeriodEnd, CreditsIncluded, CreditsExtra, CreditsUsed, VariableCostLimitCop, VariableCostExtraCop, VariableCostUsedCop, Status, CreatedAt, UpdatedAt)
    VALUES (@SubscriptionId, @BusinessId, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1), DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)), 1000, 0, 0, 0, 0, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF (SELECT COUNT(*) FROM dbo.BusinessSubscriptions WHERE BusinessId = @BusinessId AND Status IN (1, 2, 3)) <> 1
    THROW 51000, 'SeedInmobiliariaDemo: debe existir exactamente una suscripción activa.', 1;
GO
