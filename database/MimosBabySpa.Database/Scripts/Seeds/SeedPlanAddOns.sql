-- ============================================================

-- Script: SeedPlanAddOns

-- Complementos para planes de Mimos Baby Spa.

--   - Mantiene Decoracion Sencilla y Bouquet disponibles para todos los planes.

--   - Crea opciones de fotografia con precio visible, sujeto a disponibilidad.

--   - Las opciones de fotografia no suman al total de checkout; su precio es informativo.

-- ============================================================



SET QUOTED_IDENTIFIER ON;

SET NOCOUNT ON;



DECLARE @BusinessId UNIQUEIDENTIFIER;

DECLARE @PlanCategoryId UNIQUEIDENTIFIER;

DECLARE @PhotographyServiceId UNIQUEIDENTIFIER;

DECLARE @PhotographyDescription NVARCHAR(MAX) = N'Complemento de fotografía para recordar este momento. La fecha y hora del plan se validarán con la disponibilidad del fotógrafo; después de la reserva se enviará una confirmación adicional antes de dejar este complemento en firme. Este valor no se suma automáticamente al total de la reserva.';



DECLARE biz_cursor CURSOR LOCAL FAST_FORWARD FOR

    SELECT b.BusinessId

    FROM dbo.Businesses b

    WHERE EXISTS (

        SELECT 1

        FROM dbo.ServiceCategories sc

        WHERE sc.BusinessId = b.BusinessId

          AND sc.Name = N'Plan'

    );



OPEN biz_cursor;

FETCH NEXT FROM biz_cursor INTO @BusinessId;



WHILE @@FETCH_STATUS = 0

BEGIN

    SELECT TOP 1 @PlanCategoryId = sc.ServiceCategoryId

    FROM dbo.ServiceCategories sc

    WHERE sc.BusinessId = @BusinessId

      AND sc.Name = N'Plan'

    ORDER BY sc.DisplayOrder, sc.ServiceCategoryId;



    IF @PlanCategoryId IS NOT NULL

    BEGIN

        DECLARE @PhotographyOptions TABLE

        (

            AddOnName NVARCHAR(200) NOT NULL,

            Price DECIMAL(18, 2) NOT NULL,

            DisplayOrder INT NOT NULL

        );



        DELETE FROM @PhotographyOptions;

        INSERT INTO @PhotographyOptions (AddOnName, Price, DisplayOrder)

        VALUES

            (N'Fotos digitales', 50000, 3),

            (N'Fotos digitales + 2 fotos impresas', 60000, 4),

            (N'Fotos digitales + video 1 minuto', 70000, 5),

            (N'Fotos digitales + video 1 minuto + 3 fotos impresas', 80000, 6);



        DECLARE @AddOnName NVARCHAR(200);

        DECLARE @AddOnPrice DECIMAL(18, 2);



        DECLARE photo_cursor CURSOR LOCAL FAST_FORWARD FOR

            SELECT AddOnName, Price

            FROM @PhotographyOptions

            ORDER BY DisplayOrder;



        OPEN photo_cursor;

        FETCH NEXT FROM photo_cursor INTO @AddOnName, @AddOnPrice;



        WHILE @@FETCH_STATUS = 0

        BEGIN

            SELECT @PhotographyServiceId = s.ServiceId

            FROM dbo.Services s

            WHERE s.BusinessId = @BusinessId

              AND s.ServiceName = @AddOnName;



            IF @PhotographyServiceId IS NULL

            BEGIN

                SET @PhotographyServiceId = NEWID();



                INSERT INTO dbo.Services

                    (ServiceId, BusinessId, ServiceName, Description, DurationMinutes,

                     Price, IncludeInCheckoutTotal, CategoryId, Tier, ServiceType, FulfillmentKind, IsActive, CreatedAt)

                VALUES

                    (@PhotographyServiceId, @BusinessId, @AddOnName,

                     @PhotographyDescription,

                     0, @AddOnPrice, 0, @PlanCategoryId, 0, 1, 0, 1, GETUTCDATE());

            END

            ELSE

            BEGIN

                UPDATE dbo.Services

                SET Description = @PhotographyDescription,

                    Price = @AddOnPrice,

                    IncludeInCheckoutTotal = 0,

                    DurationMinutes = 0,

                    CategoryId = @PlanCategoryId,

                    ServiceType = 1,

                    IsActive = 1,

                    UpdatedAt = GETUTCDATE()

                WHERE ServiceId = @PhotographyServiceId;

            END



            SET @PhotographyServiceId = NULL;

            FETCH NEXT FROM photo_cursor INTO @AddOnName, @AddOnPrice;

        END



        CLOSE photo_cursor;

        DEALLOCATE photo_cursor;



        DELETE rules

        FROM dbo.ServiceAddOnRules rules

        INNER JOIN dbo.Services oldPhoto

            ON oldPhoto.ServiceId = rules.AddOnServiceId

        WHERE oldPhoto.BusinessId = @BusinessId

          AND oldPhoto.ServiceName = N'Fotografía';



        UPDATE dbo.Services

        SET IsActive = 0,

            UpdatedAt = GETUTCDATE()

        WHERE BusinessId = @BusinessId

          AND ServiceName = N'Fotografía';



        DECLARE @PlanAddOns TABLE

        (

            AddOnName NVARCHAR(200) NOT NULL,

            DisplayOrder INT NOT NULL

        );



        DELETE FROM @PlanAddOns;

        INSERT INTO @PlanAddOns (AddOnName, DisplayOrder)

        VALUES

            (N'Decoración Sencilla', 1),

            (N'Decoración Bouquet Personalizado', 2),

            (N'Fotos digitales', 3),

            (N'Fotos digitales + 2 fotos impresas', 4),

            (N'Fotos digitales + video 1 minuto', 5),

            (N'Fotos digitales + video 1 minuto + 3 fotos impresas', 6);



        INSERT INTO dbo.ServiceAddOnRules

            (ServiceAddOnRuleId, BusinessId, AddOnServiceId, CompatibleServiceId, DisplayOrder)

        SELECT

            NEWID(),

            @BusinessId,

            addon.ServiceId,

            planService.ServiceId,

            planAddon.DisplayOrder

        FROM @PlanAddOns planAddon

        INNER JOIN dbo.Services addon

            ON addon.BusinessId = @BusinessId

           AND addon.ServiceName = planAddon.AddOnName

           AND addon.ServiceType = 1

           AND addon.IsActive = 1

        INNER JOIN dbo.Services planService

            ON planService.BusinessId = @BusinessId

           AND planService.CategoryId = @PlanCategoryId

           AND planService.ServiceType = 0

           AND planService.IsActive = 1

        WHERE NOT EXISTS (

            SELECT 1

            FROM dbo.ServiceAddOnRules existing

            WHERE existing.BusinessId = @BusinessId

              AND existing.AddOnServiceId = addon.ServiceId

              AND existing.CompatibleServiceId = planService.ServiceId

        );

    END



    SET @PlanCategoryId = NULL;

    SET @PhotographyServiceId = NULL;



    FETCH NEXT FROM biz_cursor INTO @BusinessId;

END



CLOSE biz_cursor;

DEALLOCATE biz_cursor;



PRINT N'Seed de complementos para planes completado.';

GO

