-- Script para poblar recursos y servicios por defecto para el negocio de ejemplo
-- Ejecutar después de crear las tablas

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222'; -- BusinessId por defecto

-- Verificar que el negocio existe
IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
BEGIN
    PRINT 'El BusinessId especificado no existe. Por favor verifica el BusinessId.';
    RETURN;
END

-- 1. Crear recursos del negocio
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

-- 2. Crear servicios
DECLARE @MarineritosId UNIQUEIDENTIFIER;
DECLARE @AventurasMarinasId UNIQUEIDENTIFIER;
DECLARE @SuavesMimosId UNIQUEIDENTIFIER;
DECLARE @ClaseGrupalId UNIQUEIDENTIFIER;

-- Marineritos
IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Marineritos')
BEGIN
    SET @MarineritosId = NEWID();
    INSERT INTO [dbo].[Services] ([ServiceId], [BusinessId], [ServiceName], [DurationMinutes], [CreatedAt])
    VALUES (@MarineritosId, @BusinessId, 'Marineritos', 60, GETUTCDATE());
    PRINT 'Servicio Marineritos creado';
END
ELSE
BEGIN
    SELECT @MarineritosId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Marineritos';
END

-- Aventuras Marinas
IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Aventuras Marinas')
BEGIN
    SET @AventurasMarinasId = NEWID();
    INSERT INTO [dbo].[Services] ([ServiceId], [BusinessId], [ServiceName], [DurationMinutes], [CreatedAt])
    VALUES (@AventurasMarinasId, @BusinessId, 'Aventuras Marinas', 60, GETUTCDATE());
    PRINT 'Servicio Aventuras Marinas creado';
END
ELSE
BEGIN
    SELECT @AventurasMarinasId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Aventuras Marinas';
END

-- Suaves Mimos
IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Suaves Mimos')
BEGIN
    SET @SuavesMimosId = NEWID();
    INSERT INTO [dbo].[Services] ([ServiceId], [BusinessId], [ServiceName], [DurationMinutes], [CreatedAt])
    VALUES (@SuavesMimosId, @BusinessId, 'Suaves Mimos', 60, GETUTCDATE());
    PRINT 'Servicio Suaves Mimos creado';
END
ELSE
BEGIN
    SELECT @SuavesMimosId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Suaves Mimos';
END

-- Clase Grupal
IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Clase Grupal')
BEGIN
    SET @ClaseGrupalId = NEWID();
    INSERT INTO [dbo].[Services] ([ServiceId], [BusinessId], [ServiceName], [DurationMinutes], [CreatedAt])
    VALUES (@ClaseGrupalId, @BusinessId, 'Clase Grupal', 60, GETUTCDATE());
    PRINT 'Servicio Clase Grupal creado';
END
ELSE
BEGIN
    SELECT @ClaseGrupalId = [ServiceId] FROM [dbo].[Services] WHERE [BusinessId] = @BusinessId AND [ServiceName] = 'Clase Grupal';
END

-- 3. Asignar uso de recursos a servicios
DECLARE @BabyGymId UNIQUEIDENTIFIER;
DECLARE @HidroId UNIQUEIDENTIFIER;
DECLARE @MasajeId UNIQUEIDENTIFIER;

SELECT @BabyGymId = [BusinessResourceId] FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Baby Gym';
SELECT @HidroId = [BusinessResourceId] FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Hidroterapia';
SELECT @MasajeId = [BusinessResourceId] FROM [dbo].[BusinessResources] WHERE [BusinessId] = @BusinessId AND [ResourceName] = 'Masaje';

-- Marineritos: Baby Gym + Hidro + Masaje
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @MarineritosId AND [BusinessResourceId] = @BabyGymId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @MarineritosId, @BabyGymId, 1);
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @MarineritosId AND [BusinessResourceId] = @HidroId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @MarineritosId, @HidroId, 1);
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @MarineritosId AND [BusinessResourceId] = @MasajeId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @MarineritosId, @MasajeId, 1);
END

-- Aventuras Marinas: Hidro + Masaje
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @AventurasMarinasId AND [BusinessResourceId] = @HidroId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @AventurasMarinasId, @HidroId, 1);
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @AventurasMarinasId AND [BusinessResourceId] = @MasajeId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @AventurasMarinasId, @MasajeId, 1);
END

-- Suaves Mimos: Hidro + Masaje
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @SuavesMimosId AND [BusinessResourceId] = @HidroId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @SuavesMimosId, @HidroId, 1);
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @SuavesMimosId AND [BusinessResourceId] = @MasajeId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @SuavesMimosId, @MasajeId, 1);
END

-- Clase Grupal: Baby Gym
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = @ClaseGrupalId AND [BusinessResourceId] = @BabyGymId)
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES (NEWID(), @ClaseGrupalId, @BabyGymId, 1);
END

-- 4. Crear reglas de coexistencia
-- Marineritos + Aventuras Marinas
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCoexistenceRules] 
               WHERE [BusinessId] = @BusinessId 
               AND [ServiceId1] = @MarineritosId 
               AND [ServiceId2] = @AventurasMarinasId)
BEGIN
    INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist], [CreatedAt])
    VALUES (NEWID(), @BusinessId, @MarineritosId, @AventurasMarinasId, 1, GETUTCDATE());
END

-- Marineritos + Suaves Mimos
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCoexistenceRules] 
               WHERE [BusinessId] = @BusinessId 
               AND [ServiceId1] = @MarineritosId 
               AND [ServiceId2] = @SuavesMimosId)
BEGIN
    INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist], [CreatedAt])
    VALUES (NEWID(), @BusinessId, @MarineritosId, @SuavesMimosId, 1, GETUTCDATE());
END

-- Aventuras Marinas + Suaves Mimos
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCoexistenceRules] 
               WHERE [BusinessId] = @BusinessId 
               AND [ServiceId1] = @AventurasMarinasId 
               AND [ServiceId2] = @SuavesMimosId)
BEGIN
    INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist], [CreatedAt])
    VALUES (NEWID(), @BusinessId, @AventurasMarinasId, @SuavesMimosId, 1, GETUTCDATE());
END

-- Aventuras Marinas + Clase Grupal
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCoexistenceRules] 
               WHERE [BusinessId] = @BusinessId 
               AND [ServiceId1] = @AventurasMarinasId 
               AND [ServiceId2] = @ClaseGrupalId)
BEGIN
    INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist], [CreatedAt])
    VALUES (NEWID(), @BusinessId, @AventurasMarinasId, @ClaseGrupalId, 1, GETUTCDATE());
END

-- Suaves Mimos + Clase Grupal
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCoexistenceRules] 
               WHERE [BusinessId] = @BusinessId 
               AND [ServiceId1] = @SuavesMimosId 
               AND [ServiceId2] = @ClaseGrupalId)
BEGIN
    INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist], [CreatedAt])
    VALUES (NEWID(), @BusinessId, @SuavesMimosId, @ClaseGrupalId, 1, GETUTCDATE());
END

-- EJEMPLO: Si "Clase Grupal" permitiera múltiples reservas simultáneas del mismo servicio,
-- crearíamos una regla donde ServiceId1 = ServiceId2:
-- INSERT INTO [dbo].[ServiceCoexistenceRules] ([ServiceCoexistenceRuleId], [BusinessId], [ServiceId1], [ServiceId2], [CanCoexist])
-- VALUES (NEWID(), @BusinessId, @ClaseGrupalId, @ClaseGrupalId, 1);
-- Esto permitiría múltiples reservas de "Clase Grupal" en el mismo horario (si hay recursos suficientes)

PRINT 'Recursos y servicios por defecto creados exitosamente';

GO
