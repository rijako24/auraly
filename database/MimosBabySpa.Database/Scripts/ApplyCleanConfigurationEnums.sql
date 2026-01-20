-- Script para aplicar la limpieza de configuraciones obsoletas
-- Ejecutar este script manualmente si la migración no se aplicó correctamente

-- 1. Eliminar configuraciones obsoletas de SystemConfiguration
DELETE FROM SystemConfigurations 
WHERE SystemConfigurationId IN (2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

-- 2. Eliminar BusinessConfigurations que ya no se usan
-- Solo se mantiene: Key 0 (BusinessInformation)
DELETE FROM BusinessConfigurations 
WHERE [Key] != 0;

-- 3. Verificar resultados
SELECT [Key], COUNT(*) as Total
FROM BusinessConfigurations
GROUP BY [Key]
ORDER BY [Key];

PRINT 'Limpieza de configuraciones obsoletas completada.';
PRINT 'Solo debe quedar la configuración con Key = 0 (BusinessInformation).';
