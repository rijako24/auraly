-- =============================================================================
-- Migration 024: Generic extraction rules — contextual variables + clean values
--
-- 1. Extracción contextual de variables: rechazos cortos ("no", "ninguno")
--    cuando el asistente preguntó por opciones; alinea con hints del campo.
-- 2. Limpieza genérica: valores sin emojis/markdown copiados del historial.
--
-- Idempotent: each block uses its own marker string in extractionInstructions.
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @FlowDefId UNIQUEIDENTIFIER = (
    SELECT TOP 1 [FlowDefinitionId] FROM [dbo].[FlowDefinitions] ORDER BY [CreatedAt] DESC
);

DECLARE @DefinitionJson NVARCHAR(MAX) = (
    SELECT [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId
);

IF @DefinitionJson IS NULL
BEGIN
    RAISERROR('FlowDefinition not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @MarkerVars NVARCHAR(120) = N'Extracción contextual de variables (rechazos y respuestas al asistente)';
DECLARE @MarkerClean NVARCHAR(120) = N'Limpieza de valores extraídos (sin decoración visual)';

DECLARE @CurrentInstr NVARCHAR(MAX);
DECLARE @VarsAppended BIT = 0;
DECLARE @CleanAppended BIT = 0;

SELECT @CurrentInstr = extractionInstructions
FROM OPENJSON(@DefinitionJson)
WITH (extractionInstructions NVARCHAR(MAX) '$.extractionInstructions');

IF @CurrentInstr IS NOT NULL AND CHARINDEX(@MarkerVars, @CurrentInstr) = 0
BEGIN
    DECLARE @VarsBlock NVARCHAR(MAX) =
        CHAR(10) + CHAR(10)
        + N'Extracción contextual de variables (rechazos y respuestas al asistente):' + CHAR(10)
        + N'- Si el asistente presentó opciones o preguntó por un campo concreto y el usuario rechaza todas las opciones o declina el extra (no, ninguno, no quiero, paso, así sin eso, etc.), aplica el hint de extracción de ese campo: si el hint indica un valor literal de rechazo (por ejemplo "ninguno"), extráelo en extracted_fields.' + CHAR(10)
        + N'- La regla de no incluir campos no mencionados se refiere a datos que no fueron objeto del intercambio. Si el asistente preguntó por ese tema y el usuario respondió (aunque sea negándose), el campo sí fue abordado: extrae según el hint del campo.';

    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.extractionInstructions',
        @CurrentInstr + @VarsBlock
    );
    SET @VarsAppended = 1;
END;

SELECT @CurrentInstr = extractionInstructions
FROM OPENJSON(@DefinitionJson)
WITH (extractionInstructions NVARCHAR(MAX) '$.extractionInstructions');

IF @CurrentInstr IS NOT NULL AND CHARINDEX(@MarkerClean, @CurrentInstr) = 0
BEGIN
    DECLARE @CleanBlock NVARCHAR(MAX) =
        CHAR(10) + CHAR(10)
        + N'Limpieza de valores extraídos (sin decoración visual):' + CHAR(10)
        + N'- Los valores en extracted_fields deben ser texto limpio: sin emojis, iconos, asteriscos de markdown, viñetas ni decoración visual copiada del historial del asistente.' + CHAR(10)
        + N'- Si el historial muestra un valor con formato decorativo, extrae solo el contenido semántico útil (texto alineado con el tipo y el hint del campo).';

    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.extractionInstructions',
        @CurrentInstr + @CleanBlock
    );
    SET @CleanAppended = 1;
END;

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @DefinitionJson,
    [UpdatedAt]      = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

SELECT @CurrentInstr = extractionInstructions
FROM OPENJSON(@DefinitionJson)
WITH (extractionInstructions NVARCHAR(MAX) '$.extractionInstructions');

PRINT '024_ExtractionVariablesContextAndValueCleanup completed.';
PRINT '  contextual variables block: ' + CASE WHEN @VarsAppended = 1 THEN 'appended' WHEN @CurrentInstr IS NOT NULL AND CHARINDEX(@MarkerVars, @CurrentInstr) > 0 THEN 'already present' ELSE 'not applied (NULL instructions?)' END;
PRINT '  value cleanup block: ' + CASE WHEN @CleanAppended = 1 THEN 'appended' WHEN @CurrentInstr IS NOT NULL AND CHARINDEX(@MarkerClean, @CurrentInstr) > 0 THEN 'already present' ELSE 'not applied (NULL instructions?)' END;

COMMIT TRANSACTION;
GO
