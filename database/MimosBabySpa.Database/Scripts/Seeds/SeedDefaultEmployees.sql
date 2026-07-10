-- Script para verificar empleados de prueba

-- Ejecutar despues de crear las tablas



DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222'; -- BusinessId por defecto



-- Verificar que el negocio existe

IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)

BEGIN

    PRINT 'El BusinessId especificado no existe. Por favor verifica el BusinessId.';

    RETURN;

END



-- Obtener IDs de empleados existentes

DECLARE @Empleado1Id UNIQUEIDENTIFIER;

DECLARE @Empleado2Id UNIQUEIDENTIFIER;



SELECT @Empleado1Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId AND [Name] = 'Empleado 1';

SELECT @Empleado2Id = [EmployeeId] FROM [dbo].[Employees] WHERE [BusinessId] = @BusinessId AND [Name] = 'Empleado 2';



-- Si no existen, verificar por indice

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

PRINT 'No se crean asociaciones de empleados a servicios de prueba.';



GO

