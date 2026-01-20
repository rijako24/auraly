-- Script para eliminar BusinessConfigurations con Key = 1 (ImportantContextFields)
-- Ahora solo existe BusinessInformation (Key: 0) con todo el contenido

-- Eliminar BusinessConfigurations con Key = 1
DELETE FROM BusinessConfigurations 
WHERE [Key] = 1;

-- Verificar resultados
SELECT [Key], COUNT(*) as Total
FROM BusinessConfigurations
GROUP BY [Key]
ORDER BY [Key];

PRINT 'Eliminación de ImportantContextFields (Key: 1) completada.';
PRINT 'Solo debe quedar la configuración con Key = 0 (BusinessInformation).';
GO
