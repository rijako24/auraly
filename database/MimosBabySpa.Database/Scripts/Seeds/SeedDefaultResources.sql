-- Script para poblar recursos por defecto para el negocio de ejemplo
-- Ejecutar despues de crear las tablas

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222'; -- BusinessId por defecto

-- Verificar que el negocio existe
IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
BEGIN
    PRINT 'El BusinessId especificado no existe. Por favor verifica el BusinessId.';
    RETURN;
END

-- Crear recursos del negocio
IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Baby Gym')
BEGIN
    INSERT INTO [dbo].[BusinessResources] ([BusinessResourceId], [BusinessId], [ResourceName], [Quantity], [CreatedAt])
    VALUES (NEWID(), @BusinessId, 'Baby Gym', 1, GETUTCDATE());
    PRINT 'Recurso Baby Gym creado';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Hidroterapia')
BEGIN
    INSERT INTO [dbo].[BusinessResources] ([BusinessResourceId], [BusinessId], [ResourceName], [Quantity], [CreatedAt])
    VALUES (NEWID(), @BusinessId, 'Hidroterapia', 2, GETUTCDATE());
    PRINT 'Recurso Hidroterapia creado';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Masaje')
BEGIN
    INSERT INTO [dbo].[BusinessResources] ([BusinessResourceId], [BusinessId], [ResourceName], [Quantity], [CreatedAt])
    VALUES (NEWID(), @BusinessId, 'Masaje', 2, GETUTCDATE());
    PRINT 'Recurso Masaje creado';
END

PRINT 'Recursos por defecto creados exitosamente';

GO
