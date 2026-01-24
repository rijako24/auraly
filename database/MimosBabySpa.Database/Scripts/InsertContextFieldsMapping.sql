-- Script para insertar/actualizar ContextFieldsMapping en BusinessConfigurations
-- Este script configura los campos específicos del negocio que deben detectarse y guardarse en el contexto

-- IMPORTANTE: Reemplaza @BusinessId con el ID real del negocio
-- Ejemplo: DECLARE @BusinessId UNIQUEIDENTIFIER = 'TU-BUSINESS-ID-AQUI';

-- Verificar si ya existe la configuración
IF EXISTS (
    SELECT 1 
    FROM BusinessConfigurations 
    WHERE BusinessId = @BusinessId 
    AND [Key] = 1 -- ContextFieldsMapping
)
BEGIN
    -- Actualizar configuración existente
    UPDATE BusinessConfigurations
    SET 
        Value = N'=== CAMPOS DEL CONTEXTO DE CONVERSACIÓN ===

INSTRUCCIONES PARA update_conversation_state:

DEBES usar esta herramienta INMEDIATAMENTE cuando el cliente mencione:

(1) Su nombre → field=''customerName''
(2) Su teléfono → field=''phone''
(3) La edad del bebé (ej: ''tiene 4 meses'', ''mi bebé tiene 6 meses'', ''tiene 1 año'') → field=''babyAgeMonths'' (convierte años a meses: 1 año = 12 meses)
(4) Un servicio o plan → field=''service''
(5) Una fecha deseada → field=''desiredDate''
(6) Una hora deseada → field=''desiredTime''
(7) Confirmación explícita de reserva → field=''reservationConfirmed''

IMPORTANTE: 
- Si el cliente dice ''mi bebé tiene X meses'' o ''tiene X meses'' o ''X meses'', DEBES llamar esta herramienta con field=''babyAgeMonths'' y value=''X'' (solo el número).
- No inventar valores aunque sea requerido.',
        Description = N'Instrucciones específicas del negocio para mapeo de campos del contexto de conversación',
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId AND [Key] = 1;
    
    PRINT 'Configuración ContextFieldsMapping actualizada para BusinessId: ' + CAST(@BusinessId AS NVARCHAR(36));
END
ELSE
BEGIN
    -- Insertar nueva configuración
    INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt, UpdatedAt)
    VALUES (
        NEWID(), -- BusinessConfigurationId
        @BusinessId,
        1, -- ContextFieldsMapping
        N'=== CAMPOS DEL CONTEXTO DE CONVERSACIÓN ===

INSTRUCCIONES PARA update_conversation_state:

DEBES usar esta herramienta INMEDIATAMENTE cuando el cliente mencione:

(1) Su nombre → field=''customerName''
(2) Su teléfono → field=''phone''
(3) La edad del bebé (ej: ''tiene 4 meses'', ''mi bebé tiene 6 meses'', ''tiene 1 año'') → field=''babyAgeMonths'' (convierte años a meses: 1 año = 12 meses)
(4) Un servicio o plan → field=''service''
(5) Una fecha deseada → field=''desiredDate''
(6) Una hora deseada → field=''desiredTime''
(7) Confirmación explícita de reserva → field=''reservationConfirmed''

IMPORTANTE: 
- Si el cliente dice ''mi bebé tiene X meses'' o ''tiene X meses'' o ''X meses'', DEBES llamar esta herramienta con field=''babyAgeMonths'' y value=''X'' (solo el número).
- No inventar valores aunque sea requerido.',
        N'Instrucciones específicas del negocio para mapeo de campos del contexto de conversación',
        1, -- IsActive
        GETUTCDATE(),
        GETUTCDATE()
    );
    
    PRINT 'Configuración ContextFieldsMapping insertada para BusinessId: ' + CAST(@BusinessId AS NVARCHAR(36));
END
