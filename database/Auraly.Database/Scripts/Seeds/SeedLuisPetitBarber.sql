-- =============================================================================
-- SeedLuisPetitBarber.sql
--
-- Crea/actualiza el negocio Luis Petit Profesional Barber, su catalogo de
-- servicios y el agente Luis para reservas con anticipo del 100%.
-- Idempotente.
-- =============================================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF LOWER(N'$(DeploymentEnvironment)') = N'prod'
BEGIN
    PRINT N'SeedLuisPetitBarber: seed de demostración omitido en producción.';
    RETURN;
END;
DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @TenantId        UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000000';
DECLARE @BusinessId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000001';
DECLARE @AgentId         UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000002';
DECLARE @EmployeeId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000003';
DECLARE @CategoryId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000010';
DECLARE @AddOnCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000011';
DECLARE @CejasCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000012';
DECLARE @LavadoCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000013';
DECLARE @DomicilioCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000014';
DECLARE @TratamientosCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000015';
DECLARE @BarbaCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000016';
DECLARE @PeinadoCategoryId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000017';
DECLARE @AgentTypeId     UNIQUEIDENTIFIER;
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'Luis Petit Profesional Barber', N'admin@luispetitbarber.com', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET Name = N'Luis Petit Profesional Barber',
        Email = N'admin@luispetitbarber.com',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END
IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'Luis Petit Profesional Barber',
         N'Barberia profesional enfocada en elegancia, detalle, puntualidad y atencion personalizada para cada cliente.',
         N'Por configurar', N'+573117323198', N'admin@luispetitbarber.com', N'', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        Name = N'Luis Petit Profesional Barber',
        Description = N'Barberia profesional enfocada en elegancia, detalle, puntualidad y atencion personalizada para cada cliente.',
        Address = N'Por configurar',
        Phone = N'+573117323198',
        Email = N'admin@luispetitbarber.com',
        Website = N'',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories WHERE ServiceCategoryId = @CategoryId)
BEGIN
    INSERT INTO dbo.ServiceCategories
        (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
    VALUES
        (@CategoryId, @BusinessId, N'Corte de Cabello',
         N'Cortes de cabello, cortes con barba, color y puntas.',
         1, 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.ServiceCategories
    SET BusinessId = @BusinessId,
        Name = N'Corte de Cabello',
        Description = N'Cortes de cabello, cortes con barba, color y puntas.',
        DisplayOrder = 1,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE ServiceCategoryId = @CategoryId;
END
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories WHERE ServiceCategoryId = @AddOnCategoryId)
BEGIN
    INSERT INTO dbo.ServiceCategories
        (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
    VALUES
        (@AddOnCategoryId, @BusinessId, N'Adicionales para cortes',
         N'Complementos opcionales que se agregan dentro del tiempo del corte.',
         99, 0, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.ServiceCategories
    SET BusinessId = @BusinessId,
        Name = N'Adicionales para cortes',
        Description = N'Complementos opcionales que se agregan dentro del tiempo del corte.',
        DisplayOrder = 99,
        IsActive = 0,
        UpdatedAt = GETUTCDATE()
    WHERE ServiceCategoryId = @AddOnCategoryId;
END
DECLARE @LiteralCategories TABLE
(
    ServiceCategoryId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    DisplayOrder INT NOT NULL
);
INSERT INTO @LiteralCategories (ServiceCategoryId, Name, Description, DisplayOrder)
VALUES
(@CejasCategoryId, N'Diseno de cejas', N'Diseno y perfilado de cejas.', 2),
(@LavadoCategoryId, N'Lavado profundo', N'Limpieza profunda del cabello y cuero cabelludo.', 3),
(@DomicilioCategoryId, N'Servicio a domicilio', N'Barberia a domicilio segun ubicacion y disponibilidad.', 4),
(@TratamientosCategoryId, N'Keratina / Tratamientos especiales', N'Tratamientos capilares con valor segun diagnostico.', 5),
(@BarbaCategoryId, N'Delineado de barba', N'Delineado y perfilado limpio de barba.', 6),
(@PeinadoCategoryId, N'Peinado premium', N'Lavado y peinado con productos de alta calidad.', 7);
MERGE dbo.ServiceCategories AS target
USING @LiteralCategories AS source
   ON target.ServiceCategoryId = source.ServiceCategoryId
WHEN MATCHED THEN
    UPDATE SET
        BusinessId = @BusinessId,
        Name = source.Name,
        Description = source.Description,
        DisplayOrder = source.DisplayOrder,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
    VALUES (source.ServiceCategoryId, @BusinessId, source.Name, source.Description, source.DisplayOrder, 1, GETUTCDATE());
UPDATE dbo.Services
SET ServiceName = N'Corte basico de nino',
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ServiceId = 'BABA0000-0000-0000-0000-000000000101'
  AND ServiceName <> N'Corte basico de nino'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Services existing
      WHERE existing.BusinessId = @BusinessId
        AND existing.ServiceName = N'Corte basico de nino'
  );
UPDATE dbo.Services
SET ServiceName = N'Corte basico de adulto',
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ServiceId = 'BABA0000-0000-0000-0000-000000000102'
  AND ServiceName <> N'Corte basico de adulto'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Services existing
      WHERE existing.BusinessId = @BusinessId
        AND existing.ServiceName = N'Corte basico de adulto'
  );
UPDATE dbo.Services
SET ServiceName = N'Corte + barba con terminacion premium',
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ServiceId = 'BABA0000-0000-0000-0000-000000000103'
  AND ServiceName <> N'Corte + barba con terminacion premium'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Services existing
      WHERE existing.BusinessId = @BusinessId
        AND existing.ServiceName = N'Corte + barba con terminacion premium'
  );
UPDATE dbo.Services
SET ServiceName = N'Diseno de cejas',
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ServiceId = 'BABA0000-0000-0000-0000-000000000104'
  AND ServiceName <> N'Diseno de cejas'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Services existing
      WHERE existing.BusinessId = @BusinessId
        AND existing.ServiceName = N'Diseno de cejas'
  );
UPDATE dbo.Services
SET ServiceName = N'Keratina / Tratamientos especiales',
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND ServiceId = 'BABA0000-0000-0000-0000-000000000107'
  AND ServiceName <> N'Keratina / Tratamientos especiales'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Services existing
      WHERE existing.BusinessId = @BusinessId
        AND existing.ServiceName = N'Keratina / Tratamientos especiales'
  );
DECLARE @Services TABLE
(
    ServiceId UNIQUEIDENTIFIER NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    Keywords NVARCHAR(1000) NULL,
    CategoryId UNIQUEIDENTIFIER NOT NULL,
    DurationMinutes INT NOT NULL,
    Price DECIMAL(18, 2) NOT NULL,
    DisplayOrder INT NOT NULL
);
INSERT INTO @Services (ServiceId, ServiceName, Description, Keywords, CategoryId, DurationMinutes, Price, DisplayOrder)
VALUES
('BABA0000-0000-0000-0000-000000000101', N'Corte basico de nino',
 N'Corte infantil con trato paciente, detalle y acabado limpio. Servicio personalizado para una experiencia comoda y puntual.',
 N'corte nino, corte infantil, corte de cabello nino, cabello nino, peluqueada nino',
 @CategoryId, 30, 25000.00, 1),
('BABA0000-0000-0000-0000-000000000102', N'Corte basico de adulto',
 N'Corte profesional basico para adulto, adaptado al estilo del cliente, con atencion al detalle y acabado limpio.',
 N'corte adulto, corte de adulto, corte de cabello adulto, cabello adulto, hombre, caballero, peluqueada adulto',
 @CategoryId, 30, 30000.00, 2),
('BABA0000-0000-0000-0000-000000000103', N'Corte + barba con terminacion premium',
 N'Corte de cabello y arreglo de barba con perfilado, simetria, peinado y productos de alta calidad.',
 N'corte barba, corte y barba, corte con barba, arreglo de barba, perfilado barba, barba premium',
 @CategoryId, 45, 40000.00, 3),
('BABA0000-0000-0000-0000-000000000104', N'Diseno de cejas',
 N'Diseno y perfilado de cejas para armonizar el rostro con un acabado natural y pulido.',
 N'cejas, diseno cejas, perfilado cejas, cejas hombre',
 @CejasCategoryId, 10, 10000.00, 4),
('BABA0000-0000-0000-0000-000000000105', N'Lavado profundo',
 N'Limpieza profunda del cabello y cuero cabelludo para una sensacion fresca, cuidada y renovada.',
 N'lavado, lavado profundo, limpieza cabello, cuero cabelludo, lavado cabello',
 @LavadoCategoryId, 20, 15000.00, 5),
('BABA0000-0000-0000-0000-000000000106', N'Servicio a domicilio',
 N'Servicio de barberia a domicilio desde $100.000 COP. El valor final y disponibilidad dependen de ubicacion, horario y condiciones del servicio.',
 N'domicilio, servicio domicilio, barberia domicilio, barbero domicilio, corte domicilio',
 @DomicilioCategoryId, 60, 100000.00, 6),
('BABA0000-0000-0000-0000-000000000107', N'Keratina / Tratamientos especiales',
 N'Keratina y tratamientos especiales desde $120.000 COP. El valor puede variar segun diagnostico, longitud, tecnica y producto requerido.',
 N'keratina, tratamientos especiales, tratamiento capilar, alisado, cabello tratamiento',
 @TratamientosCategoryId, 60, 120000.00, 7),
('BABA0000-0000-0000-0000-000000000108', N'Corte + tinte',
 N'Corte con tinte para cubrimiento de canas o tonificacion de color.',
 N'corte tinte, corte con tinte, corte de cabello tinte, cabello tinte, tinte, color, canas, coloracion',
 @CategoryId, 45, 50000.00, 8),
('BABA0000-0000-0000-0000-000000000109', N'Corte premium de adulto',
 N'Corte premium de adulto con hidratacion capilar y peinado.',
 N'corte premium adulto, corte adulto premium, corte de cabello premium adulto, cabello premium adulto, hidratacion capilar, peinado adulto, corte elegante adulto',
 @CategoryId, 45, 35000.00, 9),
('BABA0000-0000-0000-0000-000000000110', N'Corte para bebes solo puntas',
 N'Corte para bebes enfocado solo en puntas, con trato cuidadoso y tiempo breve.',
 N'corte bebe, corte bebes, corte de cabello bebe, cabello bebe, solo puntas, puntas bebe, primer corte bebe',
 @CategoryId, 20, 20000.00, 10),
('BABA0000-0000-0000-0000-000000000111', N'Delineado de barba',
 N'Delineado de barba con perfilado limpio y acabado profesional.',
 N'delineado barba, delinear barba, perfilado barba, barba',
 @BarbaCategoryId, 20, 15000.00, 11),
('BABA0000-0000-0000-0000-000000000112', N'Peinado premium',
 N'Lavado y peinado premium con productos de alta calidad.',
 N'peinado, peinado premium, lavado peinado, styling, cabello peinado',
 @PeinadoCategoryId, 20, 25000.00, 12);
MERGE dbo.Services AS target
USING @Services AS source
   ON target.BusinessId = @BusinessId
  AND target.ServiceName = source.ServiceName
WHEN MATCHED THEN
    UPDATE SET
        Description = source.Description,
        Keywords = source.Keywords,
        DurationMinutes = source.DurationMinutes,
        Price = source.Price,
        IncludeInCheckoutTotal = 1,
        CategoryId = source.CategoryId,
        Tier = 0,
        ServiceType = 0,
        FulfillmentKind = 0,
        FixedScheduleLabel = NULL,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ServiceId, BusinessId, ServiceName, Description, Keywords, DurationMinutes, Price,
            IncludeInCheckoutTotal, CategoryId, Tier, ServiceType, FulfillmentKind,
            FixedScheduleLabel, IsActive, CreatedAt)
    VALUES (source.ServiceId, @BusinessId, source.ServiceName, source.Description, source.Keywords, source.DurationMinutes, source.Price,
            1, source.CategoryId, 0, 0, 0, NULL, 1, GETUTCDATE());
UPDATE s
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND NOT EXISTS (SELECT 1 FROM @Services src WHERE src.ServiceName = s.ServiceName);
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeId = @EmployeeId)
BEGIN
    INSERT INTO dbo.Employees (EmployeeId, BusinessId, Name, IsActive, CreatedAt)
    VALUES (@EmployeeId, @BusinessId, N'Luis Petit', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Employees
    SET BusinessId = @BusinessId,
        Name = N'Luis Petit',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE EmployeeId = @EmployeeId;
END
INSERT INTO dbo.EmployeeServices (EmployeeServiceId, EmployeeId, ServiceId, CreatedAt)
SELECT NEWID(), @EmployeeId, s.ServiceId, GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND s.IsActive = 1
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.EmployeeServices es
      WHERE es.EmployeeId = @EmployeeId
        AND es.ServiceId = s.ServiceId
  );
DECLARE @Hours TABLE (DayOfWeek INT NOT NULL, OpenTime TIME(0) NOT NULL, CloseTime TIME(0) NOT NULL);
INSERT INTO @Hours (DayOfWeek, OpenTime, CloseTime)
VALUES
    (1, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (1, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (2, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (2, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (3, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (3, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (4, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (4, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (5, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (5, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (6, CONVERT(TIME(0), '08:30'), CONVERT(TIME(0), '12:00')),
    (6, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '19:30')),
    (0, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00'));
MERGE dbo.BusinessWorkingHours AS target
USING @Hours AS source
   ON target.BusinessId = @BusinessId
  AND target.DayOfWeek = source.DayOfWeek
  AND target.OpenTime = source.OpenTime
WHEN MATCHED THEN
    UPDATE SET CloseTime = source.CloseTime,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (BusinessWorkingHourId, BusinessId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, source.DayOfWeek, source.OpenTime, source.CloseTime, 1, GETUTCDATE());
UPDATE dbo.BusinessWorkingHours
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND NOT EXISTS (
      SELECT 1
      FROM @Hours h
      WHERE h.DayOfWeek = BusinessWorkingHours.DayOfWeek
        AND h.OpenTime = BusinessWorkingHours.OpenTime
  );
MERGE dbo.EmployeeWorkingHours AS target
USING @Hours AS source
   ON target.BusinessId = @BusinessId
  AND target.EmployeeId = @EmployeeId
  AND target.DayOfWeek = source.DayOfWeek
  AND target.OpenTime = source.OpenTime
WHEN MATCHED THEN
    UPDATE SET CloseTime = source.CloseTime,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (EmployeeWorkingHourId, BusinessId, EmployeeId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, @EmployeeId, source.DayOfWeek, source.OpenTime, source.CloseTime, 1, GETUTCDATE());
UPDATE dbo.EmployeeWorkingHours
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND EmployeeId = @EmployeeId
  AND NOT EXISTS (
      SELECT 1
      FROM @Hours h
      WHERE h.DayOfWeek = EmployeeWorkingHours.DayOfWeek
        AND h.OpenTime = EmployeeWorkingHours.OpenTime
  );
DECLARE @AddOns TABLE
(
    ServiceId UNIQUEIDENTIFIER NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    Keywords NVARCHAR(1000) NULL,
    Price DECIMAL(18, 2) NOT NULL,
    DisplayOrder INT NOT NULL
);
INSERT INTO @AddOns (ServiceId, ServiceName, Description, Keywords, Price, DisplayOrder)
VALUES
('BABA0000-0000-0000-0000-000000000201', N'Mascarilla de carbono',
 N'Adicional para cortes. Elimina piel muerta y exceso de grasa facial; se realiza dentro del tiempo del corte.',
 N'mascarilla carbono, mascarilla facial, carbono, limpieza facial, piel grasa',
 15000.00, 1),
('BABA0000-0000-0000-0000-000000000202', N'Sombreado con aerografo',
 N'Adicional para cortes que permite un acabado con mayor definicion y perfeccion.',
 N'aerografo, sombreado, sombreado aerografo, definicion corte',
 15000.00, 2),
('BABA0000-0000-0000-0000-000000000203', N'Fibra capilar',
 N'Adicional para cortes que ayuda a disimular espacios con poco volumen de cabello.',
 N'fibra capilar, fibra, volumen cabello, disimular espacios, poco cabello',
 10000.00, 3),
('BABA0000-0000-0000-0000-000000000204', N'Relajador de ondas a base de celulas madre',
 N'Adicional para cortes, aplicado dentro del tiempo del corte.',
 N'relajador ondas, ondas, celulas madre, relajador cabello',
 15000.00, 4),
('BABA0000-0000-0000-0000-000000000205', N'Masaje con gafas de relajacion ocular',
 N'Adicional de 10 minutos con gafas de relajacion ocular. Se ofrece junto con cortes compatibles.',
 N'masaje, masaje ocular, gafas relajacion, relajacion ocular, descanso ojos',
 5000.00, 5);
MERGE dbo.Services AS target
USING @AddOns AS source
   ON target.BusinessId = @BusinessId
  AND target.ServiceName = source.ServiceName
WHEN MATCHED THEN
    UPDATE SET
        Description = source.Description,
        Keywords = source.Keywords,
        DurationMinutes = 0,
        Price = source.Price,
        IncludeInCheckoutTotal = 1,
        CategoryId = @AddOnCategoryId,
        Tier = 0,
        ServiceType = 1,
        FulfillmentKind = 0,
        FixedScheduleLabel = NULL,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ServiceId, BusinessId, ServiceName, Description, Keywords, DurationMinutes, Price,
            IncludeInCheckoutTotal, CategoryId, Tier, ServiceType, FulfillmentKind,
            FixedScheduleLabel, IsActive, CreatedAt)
    VALUES (source.ServiceId, @BusinessId, source.ServiceName, source.Description, source.Keywords, 0, source.Price,
            1, @AddOnCategoryId, 0, 1, 0, NULL, 1, GETUTCDATE());
UPDATE s
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND s.CategoryId = @AddOnCategoryId
  AND NOT EXISTS (SELECT 1 FROM @AddOns src WHERE src.ServiceName = s.ServiceName);
DECLARE @CutServices TABLE (ServiceName NVARCHAR(200) NOT NULL);
INSERT INTO @CutServices (ServiceName)
VALUES
    (N'Corte basico de nino'),
    (N'Corte basico de adulto'),
    (N'Corte + barba con terminacion premium'),
    (N'Corte + tinte'),
    (N'Corte premium de adulto'),
    (N'Corte para bebes solo puntas');
DELETE rules
FROM dbo.ServiceAddOnRules rules
INNER JOIN dbo.Services addon
    ON addon.ServiceId = rules.AddOnServiceId
WHERE rules.BusinessId = @BusinessId
  AND addon.BusinessId = @BusinessId
  AND addon.CategoryId = @AddOnCategoryId;
INSERT INTO dbo.ServiceAddOnRules
    (ServiceAddOnRuleId, BusinessId, AddOnServiceId, CompatibleServiceId, DisplayOrder)
SELECT
    NEWID(),
    @BusinessId,
    addon.ServiceId,
    cut.ServiceId,
    source.DisplayOrder
FROM @AddOns source
INNER JOIN dbo.Services addon
    ON addon.BusinessId = @BusinessId
   AND addon.ServiceName = source.ServiceName
   AND addon.ServiceType = 1
   AND addon.IsActive = 1
INNER JOIN dbo.Services cut
    ON cut.BusinessId = @BusinessId
   AND cut.ServiceType = 0
   AND cut.IsActive = 1
INNER JOIN @CutServices compatible
    ON compatible.ServiceName = cut.ServiceName;
IF EXISTS (SELECT 1 FROM dbo.BusinessSchedulingSettings WHERE BusinessId = @BusinessId)
BEGIN
    UPDATE dbo.BusinessSchedulingSettings
    SET SlotIntervalMinutes = 30,
        BufferBetweenAppointmentsMinutes = 0,
        MinimumLeadTimeMinutes = 30,
        RequireEmployee = 1,
        EmployeeStrategy = N'least_versatile',
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END
ELSE
BEGIN
    INSERT INTO dbo.BusinessSchedulingSettings
        (BusinessSchedulingSettingsId, BusinessId, SlotIntervalMinutes, BufferBetweenAppointmentsMinutes, MinimumLeadTimeMinutes, RequireEmployee, EmployeeStrategy, CreatedAt)
    VALUES
        (NEWID(), @BusinessId, 30, 0, 30, 1, N'least_versatile', GETUTCDATE());
END
SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Vendedor';
IF @AgentTypeId IS NULL
BEGIN
    SET @AgentTypeId = NEWID();
    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)
    VALUES (@AgentTypeId, N'Vendedor', N'Agente de ventas y agendamiento.', 1);
END
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.66,
  "historyWindowSize": 24,
  "persona": "Eres Luis Petit, barbero profesional de BARBER KIDS MENS. Atiendes reservas por WhatsApp en primera persona, con tono cercano, profesional, puntual y amable.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## ATENCION\n\n- Usa nombres, disponibilidad, precios y datos oficiales recibidos en el turno vigente.\n- Distingue con claridad una solicitud en preparacion, un link de anticipo generado y una reserva confirmada.\n- Presenta como confirmada una reserva solo cuando el resultado oficial vigente lo indique.\n- Nunca inventes, reconstruyas ni reutilices resumenes o links de pago desde el historial.\n- Manten tono cercano, profesional y puntual.\n\n## PRESENTACION\n\n- Separa informacion y pregunta final en parrafos cortos.\n- Cuando presentes tres o mas categorias, servicios, horarios, complementos u opciones, usa lista vertical con guion.\n- Para catalogos y opciones, conserva los saltos de linea.",
  "messageSequences": {
    "reservation_confirmed": {
      "messages": [
        {
          "body": "Tu reserva en BARBER KIDS MENS con Luis Petit Profesional Barber ha sido confirmada para el {Date} a las {Time12}."
        },
        {
          "body": "Te esperamos para una experiencia personalizada, con puntualidad garantizada y atencion al detalle de principio a fin."
        }
      ]
    },
    "reservation_confirmation_request": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_confirmation_request",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}"
          ],
          "buttons": [
            {
              "id": "reservation_attendance:confirm:{job_id}",
              "title": "Confirmar"
            },
            {
              "id": "reservation_attendance:reschedule:{job_id}",
              "title": "Reprogramar"
            }
          ]
        }
      ]
    },
    "reservation_reminder": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_reminder",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}"
          ]
        }
      ]
    },
    "payment_slot_taken": {
      "messages": [
        {
          "body": "Recibimos tu pago de ${amount} {currency}. Tu comprobante quedo registrado."
        },
        {
          "body": "Lo sentimos, el horario de las {Time} ya no esta disponible. Tu pago esta seguro. Para que dia te gustaria reagendar tu reserva?"
        }
      ]
    },
    "reservation_attendance_confirmed_reply": {
      "messages": [
        {
          "body": "Muchas gracias, tu cita ha sido confirmada."
        }
      ]
    },
    "reservation_attendance_reschedule_reply": {
      "messages": [
        {
          "body": "Claro, para que dia y hora te gustaria reagendar tu cita?"
        }
      ]
    },
    "reservation_created": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_created",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}",
            "{Total}"
          ]
        }
      ]
    }
  },
  "webhooks": {
    "wompi": {
      "reservation_created": {
        "sendMessageSequence": "reservation_confirmed"
      },
      "slot_unavailable_after_payment": {
        "sendMessageSequence": "payment_slot_taken"
      }
    }
  },
  "notifications": {
    "reservation_created": {
      "enabled": true,
      "deliveries": [
        {
          "id": "internal",
          "enabled": true,
          "recipients": [
            "573042052007"
          ],
          "sendMessageSequence": "reservation_created"
        }
      ]
    }
  },
  "reservationManagement": {
    "automaticChangeFields": [
      "date",
      "time"
    ],
    "escalateChangeFields": [
      "service",
      "add_ons"
    ],
    "escalationReasonCode": "reservation_change_requires_human",
    "manageableReservationGuidance": "Cuando el cliente pida cambiar, cancelar o confirmar una reserva sin identificar una reserva por fecha, hora o servicio existente, pide que indique cual reserva. No infieras disponibilidad ni apliques cambios sobre una reserva no identificada."
  },
  "reservationAutomations": {
    "confirmation": {
      "enabled": true,
      "trigger": {
        "type": "relative",
        "hoursBefore": 24
      },
      "sendMessageSequence": "reservation_confirmation_request",
      "actions": {
        "confirm": {
          "operation": "reservation.manage",
          "arguments": {
            "action": "confirm_attendance",
            "customer_confirmed": true,
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_confirmed_reply"
        },
        "reschedule": {
          "operation": "reservation.manage",
          "arguments": {
            "action": "request_reschedule",
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_reschedule_reply"
        }
      }
    },
    "reminder": {
      "enabled": true,
      "trigger": {
        "type": "fixedLocalTime",
        "daysBefore": 0,
        "time": "08:00"
      },
      "sendMessageSequence": "reservation_reminder"
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "reservation": {
        "paymentMethods": {
          "transferencia": {
            "label": "transferencia con link de pago",
            "aliases": [
              "transferencia",
              "link de pago"
            ],
            "payment": {
              "percentage": 100
            },
            "template": "checkout_with_deposit",
            "confirmationOutcome": "reservation_created"
          }
        }
      }
    }
  },
  "templates": {
    "checkout_with_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}} {{currency}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n\nPara asegurar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\nPaga de forma segura aqui:\n{{link_url}}\n\nCuando el pago sea aprobado, tu reserva quedara confirmada automaticamente.",
    "checkout_no_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}} {{currency}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n\nConfirmas la reserva con esta informacion?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "flows": [
    {
      "id": "booking",
      "type": "primary",
      "routingGuidance": "Use this primary flow for new bookings, catalog questions, service selection, add-ons, scheduling, customer data and checkout summaries.",
      "stages": [
        {
          "id": "discovery",
          "name": "Descubrimiento",
          "goal": "Ayudar al cliente a elegir un servicio exacto del catÃ¡logo.",
          "advanceWhenFacts": [
            "service"
          ],
          "collect": [
            "service",
            "desired_date",
            "desired_time"
          ],
          "signals": [
            {
              "type": "catalog_query",
              "description": "El cliente pide ver servicios, categorÃ­as, precios o informaciÃ³n del catÃ¡logo.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "service_selection",
              "description": "Texto con el que el cliente elige o intenta elegir un servicio concreto.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "show_catalog_on_entry",
              "operation": "catalog.get_services",
              "trigger": "on_enter",
              "condition": {
                "all": [
                  {
                    "not": {
                      "signalPresent": "catalog_query"
                    }
                  },
                  {
                    "not": {
                      "signalPresent": "service_selection"
                    }
                  }
                ]
              },
              "arguments": {
                "query": "{{turn.message}}",
                "view": "auto"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "response": {
                    "guidance": "Da la bienvenida, presÃ©ntate como Luis Petit, barbero profesional, muestra el catÃ¡logo oficial devuelto y pregunta quÃ© servicio le interesa."
                  }
                }
              }
            },
            {
              "id": "answer_catalog_query",
              "operation": "catalog.get_services",
              "trigger": "on_signal",
              "signal": "catalog_query",
              "arguments": {
                "query": "{{signal.catalog_query.value}}",
                "view": "auto"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "response": {
                    "guidance": "Responde usando exclusivamente los servicios, categorÃ­as y precios devueltos por el catÃ¡logo."
                  }
                }
              }
            },
            {
              "id": "resolve_service",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "service_selection",
              "arguments": {
                "text": "{{signal.service_selection.value}}"
              },
              "onOutcome": {
                "catalog.service_resolved": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "service": "service"
                      }
                    }
                  ]
                },
                "catalog.service_unchanged": {},
                "catalog.add_on_detected": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "add_ons": "addOns"
                      }
                    }
                  ]
                },
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta solo los candidatos devueltos y pregunta cuÃ¡l servicio desea."
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que no se encontrÃ³ ese servicio y ofrece consultar el catÃ¡logo oficial."
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Captura fecha u hora aunque el cliente las dÃ© antes de elegir servicio. Habla Ãºnicamente con datos vigentes del catÃ¡logo. No confirmes un servicio hasta que el outcome de resoluciÃ³n lo haya guardado."
        },
        {
          "id": "add_ons",
          "name": "Complementos",
          "goal": "Ofrecer adicionales compatibles cuando apliquen.",
          "advanceWhenFacts": [
            "add_ons"
          ],
          "collect": [
            "add_ons"
          ],
          "signals": [
            {
              "type": "catalog_selection",
              "description": "El cliente elige, rechaza o corrige un servicio o complemento del catÃ¡logo.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "resolve_catalog_selection",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "catalog_selection",
              "arguments": {
                "text": "{{signal.catalog_selection.value}}"
              },
              "onOutcome": {
                "catalog.service_resolved": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "service": "service"
                      }
                    }
                  ]
                },
                "catalog.service_unchanged": {},
                "catalog.add_on_detected": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "add_ons": "addOns"
                      }
                    }
                  ]
                },
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l servicio o adicional desea."
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l servicio o adicional del catÃ¡logo desea."
                  }
                }
              }
            },
            {
              "id": "get_compatible_add_ons",
              "operation": "catalog.get_compatible_add_ons",
              "condition": {
                "factMissing": "add_ons"
              },
              "arguments": {
                "service": "{{fact.service}}"
              },
              "onOutcome": {
                "catalog.add_ons_available": {
                  "response": {
                    "guidance": "Presenta Ãºnicamente los adicionales compatibles devueltos y pregunta si desea agregar alguno o continuar sin ellos."
                  }
                },
                "catalog.no_add_ons": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "add_ons",
                      "value": "ninguno"
                    }
                  ]
                }
              }
            }
          ],
          "conversationGuidance": "Si el cliente rechaza adicionales claramente, guarda add_ons=ninguno. No ofrezcas adicionales que no aparezcan en el outcome vigente del catÃ¡logo."
        },
        {
          "id": "scheduling",
          "name": "Agenda",
          "goal": "Revisar disponibilidad y validar fecha y hora para una reserva por hora.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "actions": [
            {
              "id": "check_availability",
              "operation": "reservation.check_availability",
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}"
              },
              "onOutcome": {
                "availability.exact_time_available": {
                  "response": {
                    "guidance": "Confirma brevemente que el horario estÃ¡ disponible y continÃºa con los datos faltantes."
                  }
                },
                "availability.options_available": {
                  "response": {
                    "mode": "continue"
                  }
                },
                "availability.requested_time_unavailable": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "availability.none": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que no hay espacios ese dÃ­a y pregunta por otra fecha."
                  }
                },
                "input.invalid_date": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una fecha vÃ¡lida."
                  }
                },
                "input.past_date": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una fecha de hoy en adelante."
                  }
                },
                "input.invalid_time": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una hora vÃ¡lida."
                  }
                },
                "catalog.service_unresolved": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide que elija nuevamente un servicio del catÃ¡logo vigente."
                  }
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "availability_verified",
              "priority": 10,
              "condition": {
                "verificationActive": "availability_checked"
              },
              "to": "customer_data"
            }
          ],
          "conversationGuidance": "Si falta fecha, pregunta el dÃ­a. Si hay fecha pero falta hora, la operaciÃ³n configurada muestra los espacios disponibles mediante template exclusivo. Si el cliente da una hora exacta, la operaciÃ³n valida ese horario. No afirmes disponibilidad sin el outcome vigente."
        },
        {
          "id": "customer_data",
          "name": "Datos del cliente",
          "goal": "Recoger los datos minimos para preparar el anticipo.",
          "advanceWhenFacts": [
            "customer_name",
            "customer_phone",
            "customer_birth_date"
          ],
          "collect": [
            "customer_name",
            "customer_phone",
            "customer_birth_date",
            "customer_email"
          ],
          "conversationGuidance": "Resume brevemente servicio, fecha y hora como datos para continuar. El estado sigue siendo solicitud en preparacion; disponibilidad validada solo permite continuar hacia anticipo. Pide en un solo mensaje solo los datos requeridos que falten. Conserva los datos ya presentes."
        },
        {
          "id": "finalization",
          "name": "Cierre con anticipo",
          "goal": "Preparar el resumen, generar el link de anticipo y esperar confirmacion automatica de pago.",
          "actions": [
            {
              "id": "prepare_authoritative_checkout",
              "operation": "reservation.prepare_checkout",
              "condition": {
                "all": [
                  {
                    "factPresent": "service"
                  },
                  {
                    "factPresent": "add_ons"
                  },
                  {
                    "factPresent": "desired_date"
                  },
                  {
                    "factPresent": "desired_time"
                  },
                  {
                    "factPresent": "customer_name"
                  },
                  {
                    "factPresent": "customer_phone"
                  },
                  {
                    "factPresent": "customer_birth_date"
                  },
                  {
                    "verificationActive": "availability_checked"
                  },
                  {
                    "verificationMissing": "checkout_prepared"
                  }
                ]
              },
              "arguments": {
                "service": "{{fact.service}}",
                "add_ons": "{{fact.add_ons}}",
                "context": {
                  "date": "{{fact.desired_date}}",
                  "time": "{{fact.desired_time}}",
                  "customer_name": "{{fact.customer_name}}",
                  "customer_phone": "{{fact.customer_phone}}",
                  "customer_birth_date": "{{fact.customer_birth_date}}"
                }
              }
            },
            {
              "id": "create_confirmed_no_payment_reservation",
              "operation": "reservation.create",
              "condition": {
                "all": [
                  {
                    "factEquals": {
                      "key": "customer_confirmed",
                      "value": true
                    }
                  },
                  {
                    "verificationActive": "checkout_no_payment_prepared"
                  }
                ]
              },
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}",
                "customer_name": "{{fact.customer_name}}",
                "customer_phone": "{{fact.customer_phone}}",
                "customer_email": "{{fact.customer_email}}",
                "add_ons": "{{fact.add_ons}}",
                "customer_confirmed": true
              },
              "onOutcome": {
                "reservation.created": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ]
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "revalidate_changed_schedule",
              "priority": 100,
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "to": "scheduling"
            }
          ],
          "advanceWhenFacts": [],
          "collect": [
            "service",
            "desired_date",
            "desired_time",
            "customer_name",
            "customer_phone",
            "customer_birth_date",
            "customer_confirmed"
          ],
          "conversationGuidance": "El motor prepara y presenta el checkout autoritativo cuando los datos y la disponibilidad vigente estan listos. No reconstruyas resumenes ni links desde el historial. Si cambia servicio, complementos, fecha u hora, usa solo los facts vigentes y espera la nueva validacion deterministica. Antes de pago aprobado o reserva creada, habla de solicitud o link pendiente. Para checkout sin pago, solicita confirmacion verbal antes de crear la reserva."
        }
      ]
    },
    {
      "id": "reservation_management",
      "type": "secondary",
      "ttlSeconds": 900,
      "routingGuidance": "Use only when the customer clearly wants to manage an existing reservation: view it, confirm attendance, cancel it, or change its date, time, service or add-ons. Do not use it for an open booking request, a pending checkout summary or a pending payment link.",
      "stages": [
        {
          "id": "reservation_management",
          "name": "Gestion de reserva existente",
          "goal": "Gestionar una reserva existente sin mezclarla con una solicitud nueva.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "signals": [
            {
              "type": "reservation_management_request",
              "description": "Solicitud explÃ­cita para consultar, cambiar, confirmar o cancelar una reserva existente. Usa apply_change solo cuando el cliente pide aplicar el cambio; no inventes reservation_id.",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "action": {
                    "type": "string",
                    "enum": [
                      "request_reschedule",
                      "preview_change",
                      "apply_change",
                      "confirm_attendance",
                      "cancel"
                    ]
                  },
                  "reservation_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "payment_transaction_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "job_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "service": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "date": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "time": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "add_ons": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "add_ons_mode": {
                    "type": [
                      "string",
                      "null"
                    ],
                    "enum": [
                      "add",
                      "remove",
                      "replace",
                      null
                    ]
                  },
                  "customer_confirmed": {
                    "type": [
                      "boolean",
                      "null"
                    ]
                  },
                  "notes": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "action",
                  "reservation_id",
                  "payment_transaction_id",
                  "job_id",
                  "service",
                  "date",
                  "time",
                  "add_ons",
                  "add_ons_mode",
                  "customer_confirmed",
                  "notes"
                ]
              }
            }
          ],
          "actions": [
            {
              "id": "list_reservations_on_entry",
              "operation": "reservation.list",
              "trigger": "on_enter",
              "arguments": {},
              "onOutcome": {
                "reservation.listed": {
                  "response": {
                    "guidance": "Usa Ãºnicamente las reservas devueltas para identificar la solicitud del cliente; nunca pidas UUID."
                  }
                }
              }
            },
            {
              "id": "manage_reservation_request",
              "operation": "reservation.manage",
              "trigger": "on_signal",
              "signal": "reservation_management_request",
              "arguments": {
                "action": "{{signal.reservation_management_request.value.action}}",
                "reservation_id": "{{signal.reservation_management_request.value.reservation_id}}",
                "payment_transaction_id": "{{signal.reservation_management_request.value.payment_transaction_id}}",
                "job_id": "{{signal.reservation_management_request.value.job_id}}",
                "service": "{{signal.reservation_management_request.value.service}}",
                "date": "{{signal.reservation_management_request.value.date}}",
                "time": "{{signal.reservation_management_request.value.time}}",
                "add_ons": "{{signal.reservation_management_request.value.add_ons}}",
                "add_ons_mode": "{{signal.reservation_management_request.value.add_ons_mode}}",
                "customer_confirmed": "{{signal.reservation_management_request.value.customer_confirmed}}",
                "notes": "{{signal.reservation_management_request.value.notes}}"
              },
              "onOutcome": {
                "reservation.managed": {
                  "response": {
                    "guidance": "Comunica Ãºnicamente el resultado devuelto por la operaciÃ³n de gestiÃ³n de reserva."
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Este flow solo aplica a reservas existentes. Si el cliente pide cambiar fecha u hora de una reserva existente, el motor valida la disponibilidad y aplica el cambio con el nuevo dato si corresponde. Si pide cambiar servicio o adicionales de una reserva ya confirmada, el motor decide si coloca la reserva en espera y escala. Si hay varias reservas, usa Ãºnicamente las reservas vigentes devueltas por el motor o pide que la identifique por fecha, hora o servicio; nunca pidas UUID al cliente. No generes checkout nuevo para cambios de una reserva ya pagada. Si el cliente empieza una solicitud nueva, deja que el router vuelva al flow booking."
        }
      ]
    }
  ],
  "globalActions": [
    {
      "id": "human_escalation",
      "priority": 1000,
      "goal": "Escalar a una persona cuando el cliente lo pida, este inconforme, necesite cotizacion exacta de servicio variable o la solicitud salga del alcance del bot.",
      "conversationGuidance": "Detecta ?nicamente solicitudes expl?citas de atenci?n humana o situaciones configuradas que requieren intervenci?n.",
      "signal": {
        "type": "human_escalation",
        "description": "Solicitud expl?cita de hablar con una persona, inconformidad que requiere intervenci?n o caso fuera del alcance configurado.",
        "valueSchema": {
          "type": "boolean"
        }
      },
      "actions": [
        {
          "id": "request_human",
          "operation": "escalation.request_human",
          "trigger": "on_signal",
          "signal": "human_escalation",
          "arguments": {
            "reason": "{{turn.message}}",
            "last_user_message": "{{turn.message}}"
          },
          "onOutcome": {
            "escalation.requested": {
              "effects": [
                {
                  "type": "escalation.human",
                  "reason": "customer_request"
                }
              ],
              "response": {
                "mode": "deterministic",
                "guidance": "Informa brevemente que ser? atendido por una persona."
              }
            },
            "escalation.notification_failed": {
              "response": {
                "mode": "deterministic",
                "guidance": "Informa que registrar?s la solicitud para atenci?n humana sin prometer un tiempo exacto."
              }
            }
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio",
      "type": "string",
      "required": true,
      "source": "user",
      "valueSource": "catalog",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "desired_date",
      "role": "booking.date",
      "label": "fecha deseada",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "desired_time",
      "role": "booking.time",
      "label": "hora deseada",
      "type": "time",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "availability_checked",
      "role": "booking.availability_checked",
      "label": "disponibilidad validada",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "ephemeral",
      "retentionDays": 1,
      "expireOnBusinessDayChange": true,
      "dependsOn": [
        "service",
        "desired_date",
        "desired_time"
      ]
    },
    {
      "key": "service_notes",
      "role": "booking.notes",
      "label": "notas del servicio",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "add_ons",
      "role": "booking.addons",
      "label": "adicionales",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true,
      "dependsOn": [
        "service"
      ]
    },
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del cliente",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "telefono del cliente",
      "type": "phone",
      "required": true,
      "source": "channel",
      "scope": "customer"
    },
    {
      "key": "customer_birth_date",
      "role": "customer.birth_date",
      "label": "fecha de cumpleanos del cliente",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "customer_email",
      "role": "customer.email",
      "label": "email del cliente",
      "type": "email",
      "required": false,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "request",
      "expireOnBusinessDayChange": true
    },
    {
      "key": "customer_confirmed",
      "role": "confirmation.verbal",
      "label": "confirmacion verbal del cliente",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "dependsOn": [
        "service",
        "add_ons",
        "desired_date",
        "desired_time",
        "customer_name",
        "customer_phone",
        "customer_birth_date"
      ]
    }
  ],
  "escalations": {
    "human": {
      "contacts": [
        "+573042052007"
      ]
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  }
}';
IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedLuisPetitBarber: SettingsJson invalido.', 1;
END
IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, Model, Temperature, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Luis',
         N'Agente de reservas de Luis Petit Profesional Barber con agenda por hora, anticipo del 100% y notificaciones de reserva.',
         1, @SettingsJson, N'gpt-4.1-mini', 0.66, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        Name = N'Luis',
        Description = N'Agente de reservas de Luis Petit Profesional Barber con agenda por hora, anticipo del 100% y notificaciones de reserva.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        Model = N'gpt-4.1-mini',
        Temperature = 0.66,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END
DECLARE @LuisPhoneNumber NVARCHAR(20) = N'+573117323198';
DECLARE @LuisWhatsAppPhoneId NVARCHAR(100) = N'1234810033044432';
DECLARE @LuisWhatsAppBusinessAccountId NVARCHAR(100);
DECLARE @LuisWhatsAppAccessToken NVARCHAR(500);
DECLARE @LuisWhatsAppNumberId UNIQUEIDENTIFIER;
SELECT TOP (1)
    @LuisWhatsAppNumberId = BusinessWhatsAppNumberId,
    @LuisWhatsAppAccessToken = WhatsAppAccessToken,
    @LuisWhatsAppBusinessAccountId = WhatsAppBusinessAccountId
FROM dbo.BusinessWhatsAppNumbers
WHERE (WhatsAppPhoneNumberId = @LuisWhatsAppPhoneId OR BusinessId = @BusinessId)
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY
    CASE WHEN WhatsAppPhoneNumberId = @LuisWhatsAppPhoneId THEN 0 ELSE 1 END,
    IsActive DESC,
    CreatedAt DESC;
IF @LuisWhatsAppAccessToken IS NULL
BEGIN
    PRINT N'SeedLuisPetitBarber: no se encontro token propio de Luis; omitiendo numero WhatsApp.';
END
ELSE IF @LuisWhatsAppNumberId IS NULL
BEGIN
    INSERT INTO dbo.BusinessWhatsAppNumbers (
        BusinessWhatsAppNumberId,
        BusinessId,
        AgentId,
        PhoneNumber,
        WhatsAppBusinessAccountId,
        WhatsAppPhoneNumberId,
        WhatsAppAccessToken,
        IsActive,
        CreatedAt
    )
    VALUES (
        NEWID(),
        @BusinessId,
        @AgentId,
        @LuisPhoneNumber,
        @LuisWhatsAppBusinessAccountId,
        @LuisWhatsAppPhoneId,
        @LuisWhatsAppAccessToken,
        1,
        GETUTCDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.BusinessWhatsAppNumbers
    SET BusinessId = @BusinessId,
        AgentId = @AgentId,
        PhoneNumber = @LuisPhoneNumber,
        WhatsAppPhoneNumberId = @LuisWhatsAppPhoneId,
        WhatsAppBusinessAccountId = @LuisWhatsAppBusinessAccountId,
        WhatsAppAccessToken = @LuisWhatsAppAccessToken,
        IsActive = 1
    WHERE BusinessWhatsAppNumberId = @LuisWhatsAppNumberId;
END
DECLARE @ExistingLuisWompiId UNIQUEIDENTIFIER;
DECLARE @SourceWompiConnectionId UNIQUEIDENTIFIER;
SELECT @ExistingLuisWompiId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @BusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;
SELECT @SourceWompiConnectionId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @MimosBusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;
IF @ExistingLuisWompiId IS NULL AND @SourceWompiConnectionId IS NOT NULL
BEGIN
    MERGE dbo.IntegrationConnections AS target
    USING (
        SELECT
            @BusinessId AS BusinessId,
            ConnectionType,
            Provider,
            Capability,
            [Name],
            AccountIdentifier,
            SettingsJson,
            SecretsJson,
            IsEnabled
        FROM dbo.IntegrationConnections
        WHERE IntegrationConnectionId = @SourceWompiConnectionId
    ) AS source
       ON target.BusinessId = source.BusinessId
      AND target.ConnectionType = source.ConnectionType
      AND target.Provider = source.Provider
      AND target.Capability = source.Capability
    WHEN MATCHED THEN
        UPDATE SET
            [Name] = source.[Name],
            AccountIdentifier = source.AccountIdentifier,
            SettingsJson = source.SettingsJson,
            SecretsJson = source.SecretsJson,
            IsEnabled = source.IsEnabled,
            UpdatedAt = GETUTCDATE()
    WHEN NOT MATCHED THEN
        INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
                AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
        VALUES (NEWID(), source.BusinessId, source.ConnectionType, source.Provider, source.Capability, source.[Name],
                source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());
    PRINT N'SeedLuisPetitBarber: Wompi copiado desde Mimos para Luis Petit Profesional Barber.';
END
ELSE IF @ExistingLuisWompiId IS NOT NULL
BEGIN
    PRINT N'SeedLuisPetitBarber: Wompi propio de Luis Petit Profesional Barber preservado.';
END
ELSE
BEGIN
    PRINT N'SeedLuisPetitBarber: Wompi de Mimos no encontrado; configura pagos para habilitar anticipos.';
END
DECLARE @MimosSubscriptionId UNIQUEIDENTIFIER;
SELECT TOP (1) @MimosSubscriptionId = BusinessSubscriptionId
FROM dbo.BusinessSubscriptions
WHERE BusinessId = @MimosBusinessId
  AND Status IN (1, 2, 3)
ORDER BY CreatedAt DESC;
IF @MimosSubscriptionId IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.BusinessSubscriptions WHERE BusinessId = @BusinessId AND Status IN (1, 2, 3))
    BEGIN
        UPDATE target
        SET SubscriptionPlanId     = source.SubscriptionPlanId,
            Status                 = 1,
            CurrentPeriodStart     = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
            CurrentPeriodEnd       = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),
            PlanCodeSnapshot       = source.PlanCodeSnapshot,
            PlanNameSnapshot       = source.PlanNameSnapshot,
            MonthlyPriceCop        = source.MonthlyPriceCop,
            IncludedCredits        = source.IncludedCredits,
            MaxVariableCostCop     = source.MaxVariableCostCop,
            MaxVariableCostPercent = source.MaxVariableCostPercent,
            ExtraCredits           = source.ExtraCredits,
            ExtraVariableCostCop   = source.ExtraVariableCostCop,
            AutoRenew              = source.AutoRenew,
            UpdatedAt              = SYSUTCDATETIME()
        FROM dbo.BusinessSubscriptions target
        CROSS JOIN dbo.BusinessSubscriptions source
        WHERE source.BusinessSubscriptionId = @MimosSubscriptionId
          AND target.BusinessId = @BusinessId
          AND target.Status IN (1, 2, 3);
    END
    ELSE
    BEGIN
        INSERT INTO dbo.BusinessSubscriptions (
            BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd,
            PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits,
            MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop,
            AutoRenew
        )
        SELECT
            @BusinessId, SubscriptionPlanId, 1,
            DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
            DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),
            PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits,
            MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop,
            AutoRenew
        FROM dbo.BusinessSubscriptions
        WHERE BusinessSubscriptionId = @MimosSubscriptionId;
    END
END
PRINT N'SeedLuisPetitBarber: negocio, servicios y agente Luis configurados.';
GO
