-- Script para poblar empleados y sus servicios para pruebas
-- Ejecutar después de crear las tablas y los servicios

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222'; -- BusinessId por defecto

-- Verificar que el negocio existe
IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
BEGIN
    PRINT 'El BusinessId especificado no existe. Por favor verifica el BusinessId.';
    RETURN;
END

-- Obtener IDs de servicios existentes
DECLARE @MarineritosId UNIQUEIDENTIFIER;
DECLARE @AventurasMarinasId UNIQUEIDENTIFIER;
DECLARE @SuavesMimosId UNIQUEIDENTIFIER;
DECLARE @ClaseGrupalId UNIQUEIDENTIFIER;

SELECT @MarineritosId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Marineritos';
SELECT @AventurasMarinasId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Aventuras Marinas';
SELECT @SuavesMimosId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Suaves Mimos';
SELECT @ClaseGrupalId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Clase Grupal';

-- Obtener IDs de empleados existentes
DECLARE @Empleado1Id UNIQUEIDENTIFIER;
DECLARE @Empleado2Id UNIQUEIDENTIFIER;

SELECT @Empleado1Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId AND [Name] = 'Empleado 1';
SELECT @Empleado2Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId AND [Name] = 'Empleado 2';

-- Si no existen, verificar por índice
IF @Empleado1Id IS NULL OR @Empleado2Id IS NULL
BEGIN
    SELECT TOP 1 @Empleado1Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId ORDER BY [CreatedAt];
    SELECT TOP 1 @Empleado2Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId AND [EmployeeId] != @Empleado1Id ORDER BY [CreatedAt];
END

-- Verificar que tenemos empleados
IF @Empleado1Id IS NULL OR @Empleado2Id IS NULL
BEGIN
    PRINT 'No se encontraron empleados en la base de datos. Verifica la tabla Employees.';
    RETURN;
END

PRINT 'Empleados encontrados:';
PRINT 'Empleado 1: ' + CAST(@Empleado1Id AS NVARCHAR(50));
PRINT 'Empleado 2: ' + CAST(@Empleado2Id AS NVARCHAR(50));

-- ========================================
-- ASOCIAR EMPLEADOS A SERVICIOS
-- ========================================

-- Empleado 1: Puede dar TODOS los servicios (más polivalente)
IF @MarineritosId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado1Id AND [ServiceId] = @MarineritosId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado1Id, @MarineritosId, GETUTCDATE());
    PRINT 'Empleado 1 -> Marineritos';
END

IF @AventurasMarinasId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado1Id AND [ServiceId] = @AventurasMarinasId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado1Id, @AventurasMarinasId, GETUTCDATE());
    PRINT 'Empleado 1 -> Aventuras Marinas';
END

IF @SuavesMimosId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado1Id AND [ServiceId] = @SuavesMimosId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado1Id, @SuavesMimosId, GETUTCDATE());
    PRINT 'Empleado 1 -> Suaves Mimos';
END

IF @ClaseGrupalId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado1Id AND [ServiceId] = @ClaseGrupalId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado1Id, @ClaseGrupalId, GETUTCDATE());
    PRINT 'Empleado 1 -> Clase Grupal';
END

-- Empleado 2: Solo puede dar Suaves Mimos y Aventuras Marinas (menos polivalente)
IF @SuavesMimosId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado2Id AND [ServiceId] = @SuavesMimosId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado2Id, @SuavesMimosId, GETUTCDATE());
    PRINT 'Empleado 2 -> Suaves Mimos';
END

IF @AventurasMarinasId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = @Empleado2Id AND [ServiceId] = @AventurasMarinasId)
BEGIN
    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES (NEWID(), @Empleado2Id, @AventurasMarinasId, GETUTCDATE());
    PRINT 'Empleado 2 -> Aventuras Marinas';
END

PRINT 'Asociaciones de empleados a servicios creadas exitosamente';

GO
