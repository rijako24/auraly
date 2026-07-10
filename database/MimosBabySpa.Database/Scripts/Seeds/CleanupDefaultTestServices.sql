-- Limpia servicios de prueba del negocio dev y sus relaciones directas.



DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';



DECLARE @ServicesToDelete TABLE ([ServiceId] UNIQUEIDENTIFIER PRIMARY KEY);



INSERT INTO @ServicesToDelete ([ServiceId])

SELECT [ServiceId]

FROM [dbo].[Services]

WHERE [BusinessId] = @BusinessId

  AND [ServiceName] IN (N'Marineritos', N'Aventuras Marinas', N'Suaves Mimos', N'Clase Grupal');



IF EXISTS (SELECT 1 FROM @ServicesToDelete)

BEGIN

    DELETE sru

    FROM [dbo].[ServiceResourceUsages] sru

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = sru.[ServiceId];



    DELETE es

    FROM [dbo].[EmployeeServices] es

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = es.[ServiceId];



    DELETE ra

    FROM [dbo].[ReservationAddOns] ra

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = ra.[AddOnServiceId];



    DELETE sar

    FROM [dbo].[ServiceAddOnRules] sar

    WHERE sar.[AddOnServiceId] IN (SELECT [ServiceId] FROM @ServicesToDelete)

       OR sar.[CompatibleServiceId] IN (SELECT [ServiceId] FROM @ServicesToDelete);



    DELETE sbi

    FROM [dbo].[ServiceBundleItems] sbi

    WHERE sbi.[BundleServiceId] IN (SELECT [ServiceId] FROM @ServicesToDelete)

       OR sbi.[IncludedServiceId] IN (SELECT [ServiceId] FROM @ServicesToDelete);



    DELETE e

    FROM [dbo].[Enrollments] e

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = e.[ServiceId];



    UPDATE r

    SET [ServiceId] = NULL

    FROM [dbo].[Reservations] r

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = r.[ServiceId];





    DELETE s

    FROM [dbo].[Services] s

    INNER JOIN @ServicesToDelete d ON d.[ServiceId] = s.[ServiceId];



    PRINT 'Servicios de prueba eliminados: Marineritos, Aventuras Marinas, Suaves Mimos, Clase Grupal.';

END

ELSE

BEGIN

    PRINT 'No se encontraron servicios de prueba para eliminar.';

END



GO

