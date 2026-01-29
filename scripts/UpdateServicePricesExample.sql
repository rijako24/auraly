-- Script para actualizar precios y descripciones de servicios existentes
-- Ejecutar DESPUÉS de aplicar la migración AddPriceAndDescriptionToServices

USE BotterDb;
GO

-- Actualizar servicios con precios y descripciones de ejemplo
-- Ajusta estos valores según tu negocio

-- Ejemplo: Marineritos (30 min, $50,000 COP)
UPDATE Services
SET 
    Price = 50000,
    Description = 'Sesión de hidroterapia para bebés de 0-12 meses. Incluye música, luces y masajes suaves.'
WHERE ServiceName LIKE '%Marinerito%'
  AND Price = 0;

-- Ejemplo: Aventuras Marinas (45 min, $65,000 COP)
UPDATE Services
SET 
    Price = 65000,
    Description = 'Sesión de natación para bebés de 1-3 años. Incluye juegos acuáticos y estimulación temprana.'
WHERE ServiceName LIKE '%Aventuras Marinas%'
  AND Price = 0;

-- Ejemplo: Curso de natación (60 min, $80,000 COP)
UPDATE Services
SET 
    Price = 80000,
    Description = 'Curso completo de natación infantil. Incluye técnicas de supervivencia acuática.'
WHERE ServiceName LIKE '%Natación%'
  AND Price = 0;

-- Verificar los cambios
SELECT 
    ServiceId,
    ServiceName,
    Description,
    DurationMinutes,
    Price,
    IsActive,
    CreatedAt
FROM Services
ORDER BY ServiceName;

GO

PRINT 'Precios y descripciones actualizados exitosamente.';
PRINT 'IMPORTANTE: Ajusta los valores según tu negocio.';
