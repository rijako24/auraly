-- Bot informativo de demostración; solo DEV. El contenido ficticio vive en SettingsJson.
-- El canal debe estar asignado desde administración; publicar no traslada números.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF LOWER(N'$(DeploymentEnvironment)') <> N'dev'
BEGIN
    PRINT N'SeedAlcidesSocarrasDemo: omitido fuera de DEV.';
    RETURN;
END;

DECLARE @TenantId UNIQUEIDENTIFIER = 'EC1DE500-0000-0000-0000-000000000001';
DECLARE @BusinessId UNIQUEIDENTIFIER = 'EC1DE500-0000-0000-0000-000000000010';
DECLARE @AgentId UNIQUEIDENTIFIER = 'EC1DE500-0000-0000-0000-000000000020';
DECLARE @AgentTypeId UNIQUEIDENTIFIER = 'EC1DE500-0000-0000-0000-000000000030';
DECLARE @SubscriptionId UNIQUEIDENTIFIER = 'EC1DE500-0000-0000-0000-000000000040';
DECLARE @ChannelId UNIQUEIDENTIFIER;
DECLARE @PlanId UNIQUEIDENTIFIER;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @PeriodStart DATETIME2 = DATEFROMPARTS(YEAR(@Now), MONTH(@Now), 1);
DECLARE @PeriodEnd DATETIME2 = DATEADD(MONTH, 1, @PeriodStart);
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 20,
  "extractorHistoryWindowSize": 2,
  "persona": "Atiendes a las personas interesadas en la trayectoria y las propuestas de Alcides Socarras para la Alcaldía de Valledupar. Hablas en español colombiano, con calidez, respeto y claridad. Hablas del candidato en tercera persona. Si alguien pregunta quién responde, explica con transparencia que eres un asistente automatizado del canal.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\nRedacta de forma natural según la conversación; estas pautas se aplican a TODOS los mensajes, sin un texto fijo para repetir.\n- Usa al menos dos bloques breves en cada mensaje, separados por una línea en blanco real. Cuando haya un saludo, déjalo en su propio bloque y comienza la orientación o respuesta en otro bloque. Cada bloque desarrolla una sola idea en una o dos frases cortas. Nunca juntes varias propuestas en un párrafo corrido.\n- Al explicar propuestas, comienza con un título corto en negrita de WhatsApp, con un asterisco a cada lado. Deja una línea en blanco y presenta las ideas en dos o tres viñetas con guion, una frase corta por viñeta. No reemplaces esas viñetas por un párrafo.\n- Las respuestas habituales deben ser breves, normalmente de 50 a 90 palabras. Amplía solo cuando la consulta lo necesite o la persona pida detalles. Para un dato puntual, responde aún más corto en bloques sencillos.\n- Acompaña cada mensaje con uno o dos emojis pertinentes, sin recargar ni repetirlos en cada viñeta. No uses encabezados con # ni doble asterisco.\n- No añadas preguntas rutinarias de seguimiento, menús ni invitaciones a seguir después de responder. Pregunta únicamente para resolver una ambigüedad concreta de la consulta. Si hace falta preguntar, coloca esa pregunta sola en un bloque aparte. Una respuesta completa puede terminar simplemente al resolver la consulta.\n- Saluda al iniciar la conversación y evita repetir la presentación en los turnos siguientes. Trata las preguntas, desacuerdos y críticas con respeto, sin insistencia ni llamados a votar.\n\n## FUENTE DE INFORMACIÓN\nLa ficha siguiente es la única fuente de contenido sobre Alcides Socarras. Responde con naturalidad desde esa información, sin incluir avisos de prueba, demostración ni datos ficticios en las respuestas. Si falta un dato o el tema no está cubierto por la ficha, explica de forma breve y cordial que por ahora no cuentas con esa información y no puedes dar una respuesta precisa sobre lo consultado. No inventes datos ni prometas averiguarlos o responder más adelante. Adapta esa explicación a la pregunta, sin un mensaje fijo de rechazo. No atribuyas avales oficiales, inscripciones ni verificaciones que no consten en la ficha. Presenta las propuestas como iniciativas por desarrollar, sujetas a competencia municipal, diagnóstico, presupuesto y participación ciudadana; no como obras ejecutadas, beneficios garantizados ni compromisos aprobados.\n\n## PERFIL\nAlcides Socarras tiene 50 años y estudió Ingeniería Civil. Su trayectoria comprende 20 años en planeación, mantenimiento de infraestructura y coordinación de proyectos comunitarios. Su enfoque es escuchar a la ciudadanía, priorizar necesidades y rendir cuentas sobre los proyectos. No se han definido universidad, partido, aval, patrimonio, familia, dirección privada, historial de cargos públicos ni resultados electorales.\n\n## PROPUESTAS PARA VALLEDUPAR\n### Salud\nFortalecer la atención primaria y la prevención con jornadas comunitarias coordinadas con la red de salud. Mejorar la orientación para acceder a servicios y gestionar, ante los responsables de la red, barreras y demoras reportadas. Priorizar el mantenimiento de la infraestructura municipal de salud según diagnóstico. Estas ideas no sustituyen atención médica ni prometen citas o tratamientos individuales.\n### Educación\nPriorizar mantenimiento y condiciones básicas de los colegios oficiales, con seguimiento público. Apoyar la permanencia escolar mediante coordinación de alimentación y transporte según las necesidades y competencias del municipio. Promover formación técnica y habilidades digitales mediante alianzas por evaluar. La ficha no contiene cupos, becas ni matrículas asignadas.\n### Movilidad\nRealizar diagnósticos para priorizar mantenimiento vial, señalización e intersecciones críticas. Mejorar andenes, cruces seguros y accesibilidad peatonal. Coordinar mejoras del transporte público y educación vial con las autoridades y operadores competentes. No hay rutas, tarifas, obras ni fechas de entrega aprobadas en la ficha.\n### Seguridad\nPriorizar iluminación y recuperación de espacios públicos mediante diagnóstico comunitario. Fortalecer prevención de violencias y actividades culturales y deportivas abiertas a la comunidad. Coordinar respuesta institucional y seguimiento a reportes con las autoridades competentes, respetando derechos y debido proceso. El asistente no recibe denuncias ni ofrece atención de emergencias.\n\n## CONVERSACIÓN INFORMATIVA\nResponde con las mismas propuestas generales para cualquier persona. Orienta la conversación por la pregunta formulada, sin inferir filiación política ni intención de voto. La consulta no requiere nombre, identificación, dirección, correo, datos de salud ni opinión política. Si alguien pide registrar apoyos, gestionar un trámite, agendar, recibir dinero o consultar datos privados, explica cordialmente que este asistente solo brinda información. Para fechas de elecciones, inscripción, puestos o mesas de votación, remite a los canales oficiales de la Registraduría sin inventar fechas, direcciones ni enlaces. Ante afirmaciones sobre otras personas o campañas, aclara que esta ficha no permite verificarlas.",
  "conversationOpening": {
    "enabled": true,
    "allowQuestions": false,
    "guidance": "Saluda de forma amable, cercana y cordial, con una bienvenida breve adaptada al mensaje de la persona. Evita autopresentaciones y etiquetas sobre quién responde al saludar. Ante un simple saludo, da la bienvenida y ofrece ayuda brevemente con una frase afirmativa, sin preguntas de cierre, sin desarrollar la biografía ni enumerar el programa; no superes 35 palabras. Si la persona ya hizo una pregunta, atiende esa consulta en el mismo mensaje. Aplica las pautas generales de presentación, sin una frase de saludo obligatoria."
  },
  "failureResponses": {
    "llmUnavailable": "En este momento tengo una dificultad para responder. Por favor, vuelve a intentarlo en unos minutos. Gracias por tu paciencia."
  },
  "flows": [
    {
      "id": "informacion",
      "type": "primary",
      "routingGuidance": "Consultas informativas sobre la trayectoria de Alcides Socarras y sus propuestas para Valledupar.",
      "stages": [
        {
          "id": "consultas",
          "name": "Información y propuestas",
          "goal": "Resolver consultas desde la ficha disponible, conservando la conversación abierta para preguntas posteriores.",
          "collect": [],
          "signals": [],
          "actions": [],
          "transitions": [],
          "awaitCustomerReply": false,
          "conversationGuidance": "Desarrolla la respuesta desde la ficha disponible con el detalle que solicite la persona. Cuando la pregunta sea ambigua, pide una aclaración breve. Si cambia de tema, atiende la nueva consulta sin exigir completar un cuestionario.",
          "response": {
            "mode": "llm"
          }
        }
      ]
    }
  ],
  "factSchema": [],
  "globalActions": [],
  "templates": {},
  "messageSequences": {},
  "conversationFollowUp": {
    "enabled": false
  }
}';

IF ISJSON(@SettingsJson) <> 1
    THROW 51000, 'SeedAlcidesSocarrasDemo: configuración JSON inválida.', 1;

-- El traslado inicial fue una operación histórica. La administración es dueña
-- de la asignación vigente, incluido el agente elegido para el canal.
SELECT @ChannelId = BusinessWhatsAppNumberId
FROM dbo.BusinessWhatsAppNumbers
WHERE WhatsAppPhoneNumberId = N'1234810033044432'
  AND BusinessId = @BusinessId
  AND AgentId = @AgentId
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
  AND WhatsAppBusinessAccountId = N'4841200399440958'
  AND IsActive = 1;
IF @ChannelId IS NULL
BEGIN
    PRINT N'SeedAlcidesSocarrasDemo: canal de prueba no disponible; no se aprovisiona ni activa la demostración.';
    RETURN;
END;

BEGIN TRANSACTION;
-- No se configura la demostración sobre una reasignación administrativa concurrente.
IF NOT EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessWhatsAppNumberId=@ChannelId AND BusinessId=@BusinessId AND AgentId=@AgentId
      AND IsActive=1 AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)),N'') IS NOT NULL)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el canal cambió durante la preparación.', 1;

-- Corrige el nombre dictado inicialmente, conservando las identidades ya aprovisionadas.
UPDATE dbo.Tenants
SET TenantKey=N'@alcides-socarras',Name=N'Alcides Socarras',Email=N'demo@alcides-socarras.invalid',UpdatedAt=@Now
WHERE TenantId=@TenantId AND TenantKey=N'@el-cideso-carras' AND Name=N'El Cideso Carras';
UPDATE dbo.Businesses
SET Name=N'Alcides Socarras',Email=N'demo@alcides-socarras.invalid',UpdatedAt=@Now
WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND Name=N'El Cideso Carras';

IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId=@TenantId
    AND (TenantKey<>N'@alcides-socarras' OR Name<>N'Alcides Socarras' OR IsActive<>1))
    THROW 51000, 'SeedAlcidesSocarrasDemo: el tenant no coincide con la demostración activa.', 1;
IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantKey=N'@alcides-socarras' AND TenantId<>@TenantId)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el identificador de tenant ya tiene otro propietario.', 1;
IF EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId<>@TenantId)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el negocio no pertenece al tenant esperado.', 1;
IF EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId=@AgentId AND BusinessId<>@BusinessId)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el agente no pertenece al negocio esperado.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId=@TenantId)
    INSERT dbo.Tenants (TenantId,TenantKey,Name,Email,IsActive,CreatedAt)
    VALUES (@TenantId,N'@alcides-socarras',N'Alcides Socarras',N'demo@alcides-socarras.invalid',1,@Now);
IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId)
    INSERT dbo.Businesses (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,IsActive,CreatedAt)
    VALUES (@BusinessId,@TenantId,N'Alcides Socarras',
        N'Demostración informativa con perfil y propuestas ficticios para Valledupar.',
        N'Valledupar, Cesar, Colombia',N'573117323198',N'demo@alcides-socarras.invalid',N'',N'America/Bogota',1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.AgentTypes WHERE AgentTypeId=@AgentTypeId)
BEGIN
    INSERT dbo.AgentTypes (AgentTypeId,Name,Description,IsActive)
    VALUES (@AgentTypeId,N'Asistente informativo',N'Atención de preguntas sobre información configurada, sin transacciones.',1);
END;
IF NOT EXISTS (SELECT 1 FROM dbo.AgentTypes WHERE AgentTypeId=@AgentTypeId AND IsActive=1)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el tipo de agente está inactivo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId=@AgentId)
    INSERT dbo.Agents (AgentId,BusinessId,AgentTypeId,BotType,Name,Description,Kind,IsActive,SettingsJson,Model,Temperature,CreatedAt)
    VALUES (@AgentId,@BusinessId,@AgentTypeId,1,N'Asistente de Alcides Socarras',
        N'Asistente virtual de demostración: trayectoria y propuestas ficticias.',N'customer',1,@SettingsJson,N'gpt-4.1-mini',0.2,@Now);
ELSE
    UPDATE dbo.Agents
    SET Name=N'Asistente de Alcides Socarras',SettingsJson=@SettingsJson,UpdatedAt=@Now
    WHERE AgentId=@AgentId AND (Name<>N'Asistente de Alcides Socarras' OR SettingsJson IS NULL OR SettingsJson<>@SettingsJson);

-- Esencial es el plan predeterminado de SeedBillingPlans; no se crean cobros ni renovación automática.
SELECT @PlanId=SubscriptionPlanId FROM dbo.SubscriptionPlans WHERE Code=N'essential' AND IsActive=1;
IF @PlanId IS NULL
    THROW 51000, 'SeedAlcidesSocarrasDemo: falta el plan predeterminado activo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.BusinessSubscriptions WHERE BusinessId=@BusinessId AND Status IN (1,2,3))
    INSERT dbo.BusinessSubscriptions (
        BusinessSubscriptionId,BusinessId,SubscriptionPlanId,Status,CurrentPeriodStart,CurrentPeriodEnd,
        PlanCodeSnapshot,PlanNameSnapshot,MonthlyPriceCop,IncludedCredits,MaxVariableCostCop,MaxVariableCostPercent,
        ExtraCredits,ExtraVariableCostCop,AutoRenew,CreatedAt,UpdatedAt)
    SELECT @SubscriptionId,@BusinessId,p.SubscriptionPlanId,1,@PeriodStart,@PeriodEnd,
        p.Code,p.Name,p.MonthlyPriceCop,p.IncludedCredits,p.MaxVariableCostCop,p.MaxVariableCostPercent,
        0,0,0,@Now,@Now
    FROM dbo.SubscriptionPlans p WHERE p.SubscriptionPlanId=@PlanId;
IF (SELECT COUNT(*) FROM dbo.BusinessSubscriptions WHERE BusinessId=@BusinessId AND Status IN (1,2,3)) <> 1
    THROW 51000, 'SeedAlcidesSocarrasDemo: debe existir una única suscripción utilizable.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.BusinessSubscriptions
    WHERE BusinessId=@BusinessId AND Status=1 AND CurrentPeriodStart<=@Now AND CurrentPeriodEnd>@Now
      AND IncludedCredits>0 AND MaxVariableCostCop>0)
    THROW 51000, 'SeedAlcidesSocarrasDemo: suscripción fuera de vigencia o sin capacidad.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers
    WHERE BusinessWhatsAppNumberId=@ChannelId AND BusinessId=@BusinessId AND AgentId=@AgentId AND IsActive=1)
    THROW 51000, 'SeedAlcidesSocarrasDemo: el canal no quedó asociado al agente esperado.', 1;

COMMIT TRANSACTION;
PRINT N'SeedAlcidesSocarrasDemo: tenant y asistente informativo preparados en DEV.';

