-- ============================================================================
-- Script: Eliminar BusinessConfiguration obsoletas
-- Descripción: Elimina BusinessInformation y ContextFieldsMapping que ya no se usan
-- Fecha: 2026-01-28
-- ============================================================================

USE BotterDb;
GO

PRINT '🔍 Verificando BusinessConfigurations existentes...';
PRINT '';

-- Mostrar qué registros existen actualmente
SELECT 
    b.Name AS BusinessName,
    bc.[Key] AS ConfigKey,
    CASE bc.[Key]
        WHEN 0 THEN 'BusinessInformation (OBSOLETO)'
        WHEN 1 THEN 'ContextFieldsMapping (OBSOLETO)'
        WHEN 2 THEN 'EntityExtractionConfig (EN USO)'
        ELSE 'Desconocido'
    END AS ConfigName,
    LEN(bc.Value) AS ValueLength,
    bc.CreatedAt,
    bc.UpdatedAt
FROM BusinessConfigurations bc
JOIN Businesses b ON bc.BusinessId = b.BusinessId
ORDER BY b.Name, bc.[Key];

PRINT '';
PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '';

-- Contar registros a eliminar
DECLARE @BusinessInfoCount INT;
DECLARE @ContextFieldsCount INT;

SELECT @BusinessInfoCount = COUNT(*) 
FROM BusinessConfigurations 
WHERE [Key] = 0; -- BusinessInformation

SELECT @ContextFieldsCount = COUNT(*) 
FROM BusinessConfigurations 
WHERE [Key] = 1; -- ContextFieldsMapping

PRINT '📊 RESUMEN:';
PRINT '   • BusinessInformation (Key=0): ' + CAST(@BusinessInfoCount AS NVARCHAR(10)) + ' registro(s)';
PRINT '   • ContextFieldsMapping (Key=1): ' + CAST(@ContextFieldsCount AS NVARCHAR(10)) + ' registro(s)';
PRINT '';

IF @BusinessInfoCount = 0 AND @ContextFieldsCount = 0
BEGIN
    PRINT '✅ No hay registros obsoletos que eliminar.';
    PRINT '   La base de datos ya está limpia.';
    RETURN;
END

-- Confirmar eliminación
PRINT '⚠️  ATENCIÓN: Se eliminarán ' + CAST((@BusinessInfoCount + @ContextFieldsCount) AS NVARCHAR(10)) + ' registro(s).';
PRINT '';
PRINT '❓ ¿Continuar con la eliminación?';
PRINT '   Presiona Ctrl+C para cancelar o continúa para eliminar...';
PRINT '';

-- Esperar 3 segundos (simulación de confirmación)
WAITFOR DELAY '00:00:03';

PRINT '🗑️  Eliminando registros obsoletos...';
PRINT '';

-- Eliminar BusinessInformation (Key = 0)
IF @BusinessInfoCount > 0
BEGIN
    DELETE FROM BusinessConfigurations WHERE [Key] = 0;
    PRINT '   ✅ Eliminados ' + CAST(@BusinessInfoCount AS NVARCHAR(10)) + ' registro(s) de BusinessInformation';
END

-- Eliminar ContextFieldsMapping (Key = 1)
IF @ContextFieldsCount > 0
BEGIN
    DELETE FROM BusinessConfigurations WHERE [Key] = 1;
    PRINT '   ✅ Eliminados ' + CAST(@ContextFieldsCount AS NVARCHAR(10)) + ' registro(s) de ContextFieldsMapping';
END

PRINT '';
PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '';
PRINT '✅ LIMPIEZA COMPLETADA';
PRINT '';

-- Verificar que solo queda EntityExtractionConfig (Key = 2)
PRINT '📊 Configuraciones restantes:';
PRINT '';

SELECT 
    b.Name AS BusinessName,
    bc.[Key] AS ConfigKey,
    'EntityExtractionConfig' AS ConfigName,
    LEN(bc.Value) AS ValueLength
FROM BusinessConfigurations bc
JOIN Businesses b ON bc.BusinessId = b.BusinessId
ORDER BY b.Name, bc.[Key];

PRINT '';
PRINT '💡 NOTAS:';
PRINT '   • La información del negocio ahora viene de campos directos en la tabla Businesses';
PRINT '   • Description, Address, Phone, Email, Website, OperatingHoursJson, PaymentMethodsJson';
PRINT '   • Los prompts se generan dinámicamente con SystemPromptProvider';
PRINT '   • EntityExtractionConfig sigue siendo necesario para extracción de entidades';
PRINT '';

GO
