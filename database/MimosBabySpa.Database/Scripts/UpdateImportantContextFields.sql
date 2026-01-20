-- Script para actualizar ImportantContextFields (Key: 1) con los campos correctos
-- Ejecutar este script para actualizar todos los negocios

-- Actualizar todos los registros de ImportantContextFields existentes
UPDATE BusinessConfigurations
SET 
    Value = N'customerName,phone,babyAgeMonths,service,desiredDate,desiredTime,reservationConfirmed',
    Description = N'Información importante incluye: Nombre del cliente, Teléfono, Edad del bebé (en meses), Servicio o plan elegido, Fecha deseada para la reserva, Hora deseada para la reserva, Confirmación explícita de reserva.',
    UpdatedAt = GETUTCDATE()
WHERE [Key] = 1;

-- Si no existe el registro para algún negocio, insertarlo
-- NOTA: Reemplaza @BusinessId con el ID real del negocio o ejecuta para cada BusinessId
/*
DECLARE @BusinessId UNIQUEIDENTIFIER = 'TU-BUSINESS-ID-AQUI';

IF NOT EXISTS (SELECT 1 FROM BusinessConfigurations WHERE BusinessId = @BusinessId AND [Key] = 1)
BEGIN
    INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt)
    VALUES (
        NEWID(),
        @BusinessId,
        1,
        N'customerName,phone,babyAgeMonths,service,desiredDate,desiredTime,reservationConfirmed',
        N'Campos importantes de contexto separados por comas. Información importante incluye: Nombre del cliente, Teléfono, Edad del bebé (en meses), Servicio o plan elegido, Fecha deseada para la reserva, Hora deseada para la reserva, Confirmación explícita de reserva.',
        1,
        GETUTCDATE()
    );
END
*/

-- Verificar resultados
SELECT 
    bc.BusinessId,
    b.Name AS BusinessName,
    bc.[Key],
    bc.Value,
    bc.Description,
    bc.IsActive
FROM BusinessConfigurations bc
INNER JOIN Businesses b ON bc.BusinessId = b.BusinessId
WHERE bc.[Key] = 1
ORDER BY b.Name;

PRINT 'Actualización de ImportantContextFields completada.';
PRINT 'Campos configurados: customerName, phone, babyAgeMonths, service, desiredDate, desiredTime, reservationConfirmed';
GO
