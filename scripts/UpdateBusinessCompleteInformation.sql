-- ============================================================================
-- Script: Actualizar información completa del negocio
-- Descripción: Agrega información de contacto, horarios y métodos de pago
-- ============================================================================

USE BotterDb;
GO

-- Obtener el BusinessId (ajustar según tu base de datos)
DECLARE @BusinessId UNIQUEIDENTIFIER;
SELECT TOP 1 @BusinessId = BusinessId FROM Businesses WHERE IsActive = 1;

IF @BusinessId IS NULL
BEGIN
    PRINT 'ERROR: No se encontró ningún negocio activo.';
    RETURN;
END

PRINT 'Actualizando negocio: ' + CAST(@BusinessId AS NVARCHAR(36));

-- Actualizar Business con información completa
UPDATE Businesses 
SET 
    Description = 'Mimos Baby Spa es el primer spa acuático especializado para bebés en Bogotá. Ofrecemos experiencias únicas de hidroterapia, masajes y estimulación temprana en un ambiente seguro, higiénico y amoroso. Nuestro equipo está certificado en cuidado infantil y primeros auxilios, garantizando la seguridad y bienestar de tu bebé en todo momento.',
    
    Address = 'Cra 15 #93-77, Oficina 204, Chicó, Bogotá D.C., Colombia',
    
    Phone = '+57 300 123 4567',
    
    Email = 'hola@mimosbabyspa.com',
    
    Website = 'https://www.mimosbabyspa.com',
    
    -- Horarios con formato de horario partido (cierre al mediodía)
    OperatingHoursJson = '{
        "monday": [
            {"open": "09:00", "close": "12:00"},
            {"open": "14:00", "close": "18:00"}
        ],
        "tuesday": [
            {"open": "09:00", "close": "12:00"},
            {"open": "14:00", "close": "18:00"}
        ],
        "wednesday": [
            {"open": "09:00", "close": "18:00"}
        ],
        "thursday": [
            {"open": "09:00", "close": "12:00"},
            {"open": "14:00", "close": "18:00"}
        ],
        "friday": [
            {"open": "09:00", "close": "12:00"},
            {"open": "14:00", "close": "18:00"}
        ],
        "saturday": [
            {"open": "09:00", "close": "14:00"}
        ],
        "sunday": []
    }',
    
    -- Métodos de pago aceptados
    PaymentMethodsJson = '[
        {"name": "Efectivo", "icon": "💵"},
        {"name": "Tarjeta Débito/Crédito", "icon": "💳"},
        {"name": "Transferencia Bancaria", "icon": "🏦"},
        {"name": "Nequi", "icon": "📱"},
        {"name": "Daviplata", "icon": "📱"},
        {"name": "Bancolombia QR", "icon": "📱"}
    ]',
    
    LogoUrl = NULL, -- Actualizar cuando tengas el URL del logo
    
    UpdatedAt = GETDATE()
    
WHERE BusinessId = @BusinessId;

-- Verificar actualización
SELECT 
    BusinessId,
    Name,
    Description,
    Address,
    Phone,
    Email,
    Website,
    OperatingHoursJson,
    PaymentMethodsJson,
    UpdatedAt
FROM Businesses
WHERE BusinessId = @BusinessId;

GO

PRINT '';
PRINT '✅ Información del negocio actualizada exitosamente.';
PRINT '';
PRINT '📋 RESUMEN:';
PRINT '   • Descripción completa agregada';
PRINT '   • Dirección física agregada';
PRINT '   • Teléfono de contacto agregado';
PRINT '   • Email de contacto agregado';
PRINT '   • Website agregado';
PRINT '   • Horarios con formato de horario partido configurados';
PRINT '   • 6 métodos de pago configurados';
PRINT '';
PRINT '🔄 Siguiente paso: Ejecutar UpdateServicesCompleteDescriptions.sql';
