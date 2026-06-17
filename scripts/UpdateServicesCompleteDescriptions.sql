-- ============================================================================
-- Script: Actualizar descripciones completas de servicios
-- Descripción: Agrega precios y descripciones detalladas para cada servicio
-- ============================================================================

USE BotterDb;
GO

PRINT '🔄 Actualizando servicios con descripciones completas...';
PRINT '';

-- =================================================================
-- Plan Marineritos
-- =================================================================
UPDATE Services
SET 
    Price = 125000,
    Description = '📋 DESCRIPCIÓN:
Plan integral de Baby Spa con estimulación temprana, hidroterapia y masaje infantil.

👶 EDADES RECOMENDADAS:
0 a 12 meses (ideal desde el primer mes de vida)

⏰ HORARIOS DISPONIBLES:
• Lunes a Viernes: 9:00 AM - 5:00 PM
• Sábados: 9:00 AM - 1:00 PM
• Sesiones cada 45 minutos

✨ BENEFICIOS:
• Fortalece el sistema inmunológico
• Mejora el patrón de sueño
• Estimula el desarrollo motor
• Reduce cólicos y estreñimiento
• Favorece el vínculo madre/padre-bebé
• Relaja y tranquiliza al bebé

📌 INCLUYE:
• Flotador especializado para cuello
• Temperatura del agua controlada (32-34°C)
• Música de relajación
• Iluminación terapéutica
• Masaje post-hidroterapia de 10 minutos
• Registro fotográfico del momento

⚠️ REQUISITOS:
• El bebé debe haber comido al menos 40 minutos antes
• No debe tener fiebre ni enfermedades contagiosas
• Traer pañal de agua o pañal normal
• Se recomienda baño previo en casa',
    DurationMinutes = 60,
    UpdatedAt = GETDATE()
WHERE ServiceName LIKE '%Marinerito%';

PRINT '✅ Plan Marineritos actualizado';

-- =================================================================
-- Plan Aventuras Marinas
-- =================================================================
UPDATE Services
SET 
    Price = 100000,
    Description = '📋 DESCRIPCIÓN:
Plan de Baby Spa con hidroterapia y masaje infantil.

👶 EDADES RECOMENDADAS:
12 a 36 meses (bebés que ya sostienen cabeza y tronco)

⏰ HORARIOS DISPONIBLES:
• Lunes a Viernes: 9:00 AM - 5:00 PM
• Sábados: 9:00 AM - 1:00 PM
• Sesiones cada hora

✨ BENEFICIOS:
• Desarrollo de habilidades de supervivencia acuática
• Fortalecimiento muscular avanzado
• Mejora de coordinación y equilibrio
• Estimulación de independencia y confianza
• Socialización con otros bebés
• Prevención del miedo al agua

📌 INCLUYE:
• Chaleco salvavidas especializado
• Juguetes acuáticos didácticos
• Tobogán y área de juegos acuáticos
• Instructor de natación infantil certificado
• Música y actividades temáticas
• Ducha y vestidor privado
• Registro fotográfico y video

⚠️ REQUISITOS:
• Control de esfínteres no es necesario (se usan pañales acuáticos)
• El bebé debe poder sostener su cabeza
• Certificado médico (opcional pero recomendado)
• Traer toalla y cambio de ropa',
    DurationMinutes = 45,
    UpdatedAt = GETDATE()
WHERE ServiceName LIKE '%Aventuras Marinas%';

PRINT '✅ Plan Aventuras Marinas actualizado';

-- =================================================================
-- Plan Suaves Mimos
-- =================================================================
UPDATE Services
SET 
    ServiceName = 'Plan Suaves Mimos - Post Vacunas',
    Price = 95000,
    Description = '📋 DESCRIPCIÓN:
Plan post vacunas con hidroterapia relajante y masaje infantil. No se toca la zona de punción.

👶 EDADES RECOMENDADAS:
0 a 24 meses

⏰ HORARIOS DISPONIBLES:
• Martes y Jueves: 10:00 AM - 4:00 PM
• Sábados: 10:00 AM - 12:00 PM
• Requiere reserva previa (cupos limitados)

✨ BENEFICIOS:
• Máxima relajación para bebés con cólicos
• Alivio de tensiones musculares
• Mejora la digestión
• Promueve sueño reparador
• Reduce el estrés y la ansiedad
• Momento especial de conexión familiar

📌 INCLUYE:
• 30 min de hidroterapia suave
• 20 min de masaje terapéutico especializado
• 10 min de aromaterapia con aceites esenciales seguros
• Ambiente con cromoterapia (luces de colores)
• Música relajante personalizada
• Manta térmica post-sesión
• Aceites de masaje hipoalergénicos
• Infusión para la madre/padre

⚠️ REQUISITOS:
• Ideal para bebés con cólicos, gases o dificultad para dormir
• No apto para bebés con fiebre
• Traer ropa cómoda para el bebé
• Se recomienda alimentación ligera 1 hora antes',
    DurationMinutes = 45,
    UpdatedAt = GETDATE()
WHERE ServiceName LIKE '%Suaves Mimos%' OR ServiceName LIKE '%Post Vacunas%';

PRINT '✅ Plan Suaves Mimos actualizado';

-- =================================================================
-- Clase Grupal
-- =================================================================
UPDATE Services
SET 
    Price = 40000,
    Description = '📋 DESCRIPCIÓN:
Clase de natación en grupo para bebés y niños pequeños acompañados de sus padres. Ambiente divertido y social donde aprenderán técnicas básicas de natación mientras socializan con otros niños de su edad.

👶 EDADES RECOMENDADAS:
6 meses a 4 años (grupos divididos por edad)

⏰ HORARIOS DISPONIBLES:
• Lunes y Miércoles: 4:00 PM - 5:00 PM (Grupo 6-18 meses)
• Martes y Jueves: 4:00 PM - 5:00 PM (Grupo 19-36 meses)
• Viernes: 4:00 PM - 5:00 PM (Grupo 3-4 años)
• Sábados: 11:00 AM - 12:00 PM (Todos los grupos)
• Máximo 6 bebés por clase

✨ BENEFICIOS:
• Aprendizaje de técnicas de flotación
• Desarrollo de confianza en el agua
• Socialización con otros niños
• Vínculo padres-hijos fortalecido
• Ejercicio completo para el bebé
• Precio más accesible que sesiones privadas

📌 INCLUYE:
• Instructor certificado en natación infantil
• Chaleco salvavidas (si es requerido)
• Juguetes y material didáctico
• Certificado de participación al completar ciclo
• Acceso a vestidores familiares

⚠️ REQUISITOS:
• Inscripción previa (ciclos de 8 clases)
• Al menos un adulto debe acompañar al niño en el agua
• Pañal acuático obligatorio para menores de 2 años
• Compromiso de asistencia (mínimo 6 de 8 clases)',
    DurationMinutes = 60,
    UpdatedAt = GETDATE()
WHERE ServiceName LIKE '%Clase Grupal%' OR ServiceName LIKE '%Grupal%';

PRINT '✅ Clase Grupal actualizada';

PRINT '';
PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '✅ ACTUALIZACIÓN COMPLETADA';
PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '';

-- Verificar los cambios
SELECT 
    ServiceName,
    Price,
    DurationMinutes,
    LEFT(Description, 100) + '...' AS DescriptionPreview,
    IsActive,
    UpdatedAt
FROM Services
ORDER BY ServiceName;

PRINT '';
PRINT '📊 RESUMEN:';
PRINT '   • Plan Marineritos: 60 min - $125,000 COP';
PRINT '   • Plan Aventuras Marinas: 45 min - $100,000 COP';
PRINT '   • Plan Suaves Mimos - Post Vacunas: 45 min - $95,000 COP';
PRINT '   • Clase Grupal: 60 min - $40,000 COP';
PRINT '';
PRINT '✨ Todos los servicios ahora tienen:';
PRINT '   ✓ Precio definido';
PRINT '   ✓ Descripción completa';
PRINT '   ✓ Edades recomendadas';
PRINT '   ✓ Horarios disponibles';
PRINT '   ✓ Beneficios detallados';
PRINT '   ✓ Información de lo que incluye';
PRINT '   ✓ Requisitos previos';
PRINT '';
PRINT '🚀 El prompt ahora se alimentará con esta información rica y detallada.';

GO
