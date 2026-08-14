-- ==============================================================================
-- SCRIPT: Poblar Configuraciones del Refactor de Prompts
-- Propósito: Agregar SalesGuidance y PersonalityJson a negocios existentes
-- Fecha: 2026-01-28
-- ==============================================================================

USE BotterDb;
GO

-- ==============================================================================
-- 1. SALES GUIDANCE CONFIGURATION
-- Agregar configuración de guía de ventas para Auraly
-- ==============================================================================

DECLARE @BusinessId UNIQUEIDENTIFIER = (SELECT TOP 1 BusinessId FROM Businesses WHERE Name = 'Auraly');

IF @BusinessId IS NOT NULL
BEGIN
    -- Verificar si ya existe la configuración
    IF NOT EXISTS (SELECT 1 FROM BusinessConfigurations WHERE BusinessId = @BusinessId AND [Key] = 3) -- SalesGuidance = 3
    BEGIN
        INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt)
        VALUES (
            NEWID(),
            @BusinessId,
            3, -- SalesGuidance
            '{
  "CriticalAttributes": ["BabyAge"],
  "GuidanceText": "Antes de recomendar un plan, valida que conoces la edad del bebé.\nLa edad es clave para elegir el servicio correcto y garantizar la seguridad del bebé.",
  "ExampleQuestion": "Para poder recomendarte el plan ideal, ¿me cuentas cuántos meses tiene tu bebé? 😊",
  "AttributeUnit": "meses",
  "IsEnabled": true
}',
            'Configuración de guía de ventas - Validación de edad del bebé antes de recomendar',
            1, -- IsActive
            GETUTCDATE()
        );
        
        PRINT '✅ SalesGuidance agregada para Auraly';
    END
    ELSE
    BEGIN
        PRINT '⚠️ SalesGuidance ya existe para Auraly';
    END
END
ELSE
BEGIN
    PRINT '❌ Negocio Auraly no encontrado';
END
GO

-- ==============================================================================
-- 2. BUSINESS PERSONALITY CONFIGURATION
-- Agregar personalidad del asistente a Business.PersonalityJson
-- ==============================================================================

DECLARE @BusinessId UNIQUEIDENTIFIER = (SELECT TOP 1 BusinessId FROM Businesses WHERE Name = 'Auraly');

IF @BusinessId IS NOT NULL
BEGIN
    -- Actualizar PersonalityJson si está vacío o es {}
    DECLARE @CurrentPersonality NVARCHAR(MAX) = (SELECT PersonalityJson FROM Businesses WHERE BusinessId = @BusinessId);
    
    IF @CurrentPersonality IS NULL OR @CurrentPersonality = '{}' OR @CurrentPersonality = ''
    BEGIN
        UPDATE Businesses
        SET 
            PersonalityJson = '{
  "AssistantName": "María",
  "Gender": "Female",
  "Tone": ["Cálido", "Profesional", "Empático", "Cercano", "Amoroso"],
  "UseEmojis": true,
  "GreetingStyle": "Amigable",
  "SamplePhrases": {
    "Greeting": "¡Hola! 😊 Soy {AssistantName}, un gusto saludarte. Estoy aquí para ayudarte a encontrar el mejor plan para tu bebé.",
    "Closing": "Estoy aquí para ayudarte en todo lo que necesites 😊",
    "Thanking": "¡Gracias por confiar en {BusinessName}! 💙",
    "Concern": "Entiendo tu preocupación. Estoy aquí para acompañarte.",
    "Excitement": "¡Qué emoción! Tu bebé va a disfrutar mucho esta experiencia 💙"
  },
  "Expertise": "experta en cuidado y relajación para bebés"
}',
            UpdatedAt = GETUTCDATE()
        WHERE BusinessId = @BusinessId;
        
        PRINT '✅ PersonalityJson agregada para Auraly';
    END
    ELSE
    BEGIN
        PRINT '⚠️ PersonalityJson ya existe para Auraly';
    END
END
ELSE
BEGIN
    PRINT '❌ Negocio Auraly no encontrado';
END
GO

-- ==============================================================================
-- 3. VERIFICACIÓN
-- Mostrar las configuraciones actuales
-- ==============================================================================

PRINT '';
PRINT '==============================================================================';
PRINT 'VERIFICACIÓN DE CONFIGURACIONES';
PRINT '==============================================================================';
PRINT '';

-- Mostrar SalesGuidance
SELECT 
    b.Name AS BusinessName,
    bc.[Key] AS ConfigKey,
    bc.Description,
    bc.Value AS ConfigValue,
    bc.IsActive,
    bc.CreatedAt
FROM BusinessConfigurations bc
INNER JOIN Businesses b ON bc.BusinessId = b.BusinessId
WHERE bc.[Key] = 3 -- SalesGuidance
ORDER BY b.Name;

PRINT '';

-- Mostrar PersonalityJson
SELECT 
    Name AS BusinessName,
    PersonalityJson,
    UpdatedAt
FROM Businesses
WHERE PersonalityJson IS NOT NULL AND PersonalityJson != '{}';

PRINT '';
PRINT '✅ Script completado';
GO

-- ==============================================================================
-- EJEMPLO DE CONFIGURACIÓN GENÉRICA PARA OTROS NEGOCIOS
-- ==============================================================================

-- Para negocios que NO son spa de bebés, usar configuración genérica:

/*
-- SalesGuidance genérica
{
  "CriticalAttributes": [],
  "GuidanceText": "Recomienda los servicios que mejor se adapten a las necesidades del cliente.",
  "ExampleQuestion": "",
  "AttributeUnit": null,
  "IsEnabled": false
}

-- Personality genérica
{
  "AssistantName": "Asistente",
  "Gender": "Neutral",
  "Tone": ["Profesional", "Amable"],
  "UseEmojis": false,
  "GreetingStyle": "Profesional",
  "SamplePhrases": {
    "Greeting": "Hola, soy {AssistantName}. ¿En qué puedo ayudarte?",
    "Closing": "Estoy aquí para ayudarte en lo que necesites.",
    "Thanking": "Gracias por tu tiempo."
  },
  "Expertise": null
}
*/
