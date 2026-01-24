-- Script para insertar templates de contexto para el sistema de detección de intención
-- Estos templates se usan para construir los mensajes que se inyectan al LLM

-- Template para contexto de disponibilidad
IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [SystemConfigurationId] = 2)
BEGIN
    INSERT INTO [dbo].[SystemConfigurations] ([SystemConfigurationId], [Value], [Description], [IsActive], [CreatedAt], [UpdatedAt])
    VALUES (
        2,
        N'INFORMACIÓN DE DISPONIBILIDAD (CALCULADA POR EL SISTEMA - NO INFERIR):
- Fecha consultada: {Date}
- Hora consultada: {Time}
- ¿Está disponible? {IsAvailable}
- Reservas actuales: {CurrentReservations}
- Mensaje del sistema: {Message}

IMPORTANTE: Usa estos valores EXACTOS. NO infieras disponibilidad. NO apliques reglas propias.
Si ''¿Está disponible?'' es ''False'', el horario NO está disponible. Si es ''True'', está disponible.',
        N'Template para construir el contexto de disponibilidad que se inyecta al LLM. Placeholders: {Date}, {Time}, {IsAvailable}, {CurrentReservations}, {Message}',
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );
    PRINT 'Template de contexto de disponibilidad insertado correctamente.';
END
ELSE
BEGIN
    UPDATE [dbo].[SystemConfigurations]
    SET [Value] = N'INFORMACIÓN DE DISPONIBILIDAD (CALCULADA POR EL SISTEMA - NO INFERIR):
- Fecha consultada: {Date}
- Hora consultada: {Time}
- ¿Está disponible? {IsAvailable}
- Reservas actuales: {CurrentReservations}
- Mensaje del sistema: {Message}

IMPORTANTE: Usa estos valores EXACTOS. NO infieras disponibilidad. NO apliques reglas propias.
Si ''¿Está disponible?'' es ''False'', el horario NO está disponible. Si es ''True'', está disponible.',
        [Description] = N'Template para construir el contexto de disponibilidad que se inyecta al LLM. Placeholders: {Date}, {Time}, {IsAvailable}, {CurrentReservations}, {Message}',
        [UpdatedAt] = GETUTCDATE()
    WHERE [SystemConfigurationId] = 2;
    PRINT 'Template de contexto de disponibilidad actualizado correctamente.';
END
GO

-- Template para contexto de intención detectada
IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [SystemConfigurationId] = 3)
BEGIN
    INSERT INTO [dbo].[SystemConfigurations] ([SystemConfigurationId], [Value], [Description], [IsActive], [CreatedAt], [UpdatedAt])
    VALUES (
        3,
        N'INTENCIÓN DETECTADA POR BACKEND:
- Intent: {Intent}
- Permitir reservar: {ShouldAllowReservation}
- Fecha válida: {HasDate}
- Confirmación explícita: {IsExplicitConfirmation}
- Fecha narrativa (ignorar): {IsNarrativeDate}

IMPORTANTE:
- Si ''Permitir reservar'' = false → NUNCA llames create_reservation, incluso si el usuario lo pide.
- El backend ya validó todas las condiciones necesarias.
- Solo llama create_reservation si ''Permitir reservar'' = true.',
        N'Template para construir el contexto de intención detectada que se inyecta al LLM. Placeholders: {Intent}, {ShouldAllowReservation}, {HasDate}, {IsExplicitConfirmation}, {IsNarrativeDate}',
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );
    PRINT 'Template de contexto de intención detectada insertado correctamente.';
END
ELSE
BEGIN
    UPDATE [dbo].[SystemConfigurations]
    SET [Value] = N'INTENCIÓN DETECTADA POR BACKEND:
- Intent: {Intent}
- Permitir reservar: {ShouldAllowReservation}
- Fecha válida: {HasDate}
- Confirmación explícita: {IsExplicitConfirmation}
- Fecha narrativa (ignorar): {IsNarrativeDate}

IMPORTANTE:
- Si ''Permitir reservar'' = false → NUNCA llames create_reservation, incluso si el usuario lo pide.
- El backend ya validó todas las condiciones necesarias.
- Solo llama create_reservation si ''Permitir reservar'' = true.',
        [Description] = N'Template para construir el contexto de intención detectada que se inyecta al LLM. Placeholders: {Intent}, {ShouldAllowReservation}, {HasDate}, {IsExplicitConfirmation}, {IsNarrativeDate}',
        [UpdatedAt] = GETUTCDATE()
    WHERE [SystemConfigurationId] = 3;
    PRINT 'Template de contexto de intención detectada actualizado correctamente.';
END
GO

PRINT 'Script completado. Templates de contexto configurados correctamente.';
