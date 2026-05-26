-- Script para poblar recursos del negocio de ejemplo (sin servicios legacy duplicados).
-- Los servicios canónicos se gestionan en admin / seeds dedicados.

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
BEGIN
    PRINT 'El BusinessId especificado no existe. Por favor verifica el BusinessId.';
    RETURN;
END

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

PRINT 'Recursos por defecto listos (servicios legacy no se crean en este seed).';

GO
