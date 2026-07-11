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
DECLARE @SystemPrompt NVARCHAR(MAX) = N'';
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.66,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "Eres Luis Petit, barbero profesional de BARBER KIDS MENS. Atiendes reservas por WhatsApp en primera persona, con tono cercano, profesional, puntual y amable.",
  "policies": "## ATENCION\n\n- Usa nombres y datos oficiales del catalogo o de las herramientas del turno.\n- Vocabulario de estado: antes de checkout o reserva devueltos por herramienta, solicitud en preparacion; despues de checkout, link de anticipo generado; despues de pago o reserva devuelta por herramienta, reserva confirmada.\n- Antes de pago aprobado o reserva creada por herramienta, evita decir tu reserva, cambie tu reserva o reserva confirmada; usa solicitud, datos de la solicitud o link pendiente.\n- Nunca inventes, reconstruyas ni reutilices resumenes o links de pago desde el historial; todo resumen o link vigente debe venir de prepare_checkout o create_reservation despues de cualquier cambio relevante.\n- Manten tono cercano, profesional y puntual.\n\n## PRESENTACION\n\n- Separa informacion y pregunta final en parrafos cortos.\n- Cuando presentes tres o mas categorias, servicios, horarios, complementos u opciones, usa lista vertical con guion.\n- Para catalogos y opciones, conserva los saltos de linea.",
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
      "recipients": [
        "573042052007"
      ],
      "sendMessageSequence": "reservation_created"
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
          "tool": "manage_reservation",
          "arguments": {
            "action": "confirm_attendance",
            "customer_confirmed": true,
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_confirmed_reply"
        },
        "reschedule": {
          "tool": "manage_reservation",
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
      "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Descubrimiento",
        "goal": "Ayudar al cliente a elegir un servicio exacto del catalogo.",
        "advanceWhenFacts": [
          "service"
        ],
        "collect": [
          "booking_intent",
          "service",
          "desired_date",
          "desired_time"
        ],
        "allowedActions": [
          "get_service_catalog",
          "resolve_service_selection",
          "set_fact"
        ],
        "entryActions": [
          {
            "tool": "get_service_catalog",
            "arguments": {
              "view": "auto",
              "query": "{{user.message}}"
            }
          }
        ],
        "conversationGuidance": "Cuando respondas con catalogo en discovery, ordena el mensaje asi: saluda, da la bienvenida a BARBER KIDS MENS, presentate literalmente como: Soy Luis Petit, barbero profesional. No digas tu barbero profesional. Despues muestra el catalogo oficial desde la salida vigente de get_service_catalog y al final cierra preguntando: En que servicio estas interesado? Usa la salida vigente de get_service_catalog como fuente oficial para presentar categorias, servicios y precios. Si el cliente menciona fecha u hora, como hoy, manana o una hora concreta, capturala con set_fact antes de responder para conservarla cuando elija servicio. Si el cliente nombra una familia o necesidad amplia, presenta opciones oficiales. Si el cliente elige o parafrasea un servicio de una lista ya presentada, o nombra un servicio puntual con intencion de reservar, llama resolve_service_selection con el texto literal antes de responder; no presentes complementos ni confirmes el servicio como elegido hasta que service quede registrado. No respondas categorias, servicios ni precios sin salida vigente de get_service_catalog."
      },
      {
        "id": "add_ons",
        "name": "Complementos",
        "goal": "Ofrecer adicionales compatibles cuando apliquen.",
        "advanceWhenFacts": [
          "add_ons"
        ],
        "reentryOnFactChanged": [
          "service"
        ],
        "collect": [
          "add_ons"
        ],
        "allowedActions": [
          "get_compatible_add_ons",
          "resolve_service_selection",
          "set_fact"
        ],
        "entryActions": [
          {
            "tool": "get_compatible_add_ons",
            "arguments": {
              "service": "{{fact.service}}"
            }
          }
        ],
        "conversationGuidance": "Usa la salida vigente de get_compatible_add_ons como fuente oficial de adicionales compatibles. Si el ultimo mensaje ya trae una decision sobre adicionales, registrala con set_fact y continua sin volver a preguntar. Si el mensaje pide resumen, link o actualizar anticipo junto con una decision de adicionales, registra primero add_ons y no respondas resumen ni link desde esta etapa. Si existen adicionales y aun falta decision, presenta opciones con precio y valor breve y termina con esta pregunta simple: Quieres agregar alguno de estos adicionales o seguimos sin ellos? Si el cliente rechaza o no aplican, registra add_ons=ninguno y continua. El estado conversacional es solicitud en preparacion hasta que checkout o reserva sean devueltos por una herramienta."
      },
      {
        "id": "scheduling",
        "name": "Agenda",
        "goal": "Revisar disponibilidad y validar fecha y hora para una reserva por hora.",
        "entryActions": [
          {
            "tool": "check_availability",
            "arguments": {
              "service": "{{fact.service}}",
              "date": "{{fact.desired_date}}",
              "time": "{{fact.desired_time}}"
            },
            "when": {
              "requiredFacts": [
                "service",
                "desired_date"
              ],
              "missingFacts": [
                "availability_checked"
              ]
            }
          }
        ],
        "afterTool": [
          {
            "tool": "check_availability",
            "when": {
              "path": "data.date"
            },
            "setFact": {
              "key": "desired_date",
              "value": "{{data.date}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "desired_time",
              "value": "{{data.time}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "availability_checked",
              "value": "true"
            }
          }
        ],
        "advanceWhenFacts": [
          "availability_checked"
        ],
        "reentryOnFactChanged": [
          "service",
          "desired_date",
          "desired_time"
        ],
        "collect": [
          "desired_date",
          "desired_time"
        ],
        "allowedActions": [
          "check_availability",
          "set_fact"
        ],
        "conversationGuidance": "Valida agenda con servicio canonico. Si falta fecha, pregunta el dia. Si ya hay fecha pero falta hora, check_availability se ejecuta con service y date para mostrar espacios disponibles del dia. Si el cliente da una hora exacta, valida con check_availability usando service, date y time. Si el horario no esta disponible, presenta horarios devueltos por disponibilidad y aplica el seleccionado. Disponibilidad con slot_held=false significa horario libre para continuar la solicitud."
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
        "allowedActions": [
          "set_fact"
        ],
        "conversationGuidance": "Resume brevemente servicio, fecha y hora como datos para continuar. El estado sigue siendo solicitud en preparacion; disponibilidad validada solo permite continuar hacia anticipo. Pide en un solo mensaje solo los datos requeridos que falten. Conserva los datos ya presentes."
      },
      {
        "id": "finalization",
        "name": "Cierre con anticipo",
        "goal": "Preparar el resumen, generar el link de anticipo y esperar confirmacion automatica de pago.",
        "entryActions": [
          {
            "tool": "prepare_checkout",
            "arguments": {},
            "when": {
              "requiredFacts": [
                "service",
                "add_ons",
                "desired_date",
                "desired_time",
                "customer_name",
                "customer_birth_date",
                "availability_checked"
              ],
              "missingVerifications": [
                "checkout_prepared"
              ]
            }
          }
        ],
        "afterTool": [
          {
            "tool": "check_availability",
            "when": {
              "path": "data.date"
            },
            "setFact": {
              "key": "desired_date",
              "value": "{{data.date}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "desired_time",
              "value": "{{data.time}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "availability_checked",
              "value": "true"
            }
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
        "allowedActions": [
          "prepare_checkout",
          "create_reservation",
          "verify_payment",
          "get_service_catalog",
          "resolve_service_selection",
          "check_availability",
          "set_fact",
          "reset_flow_context",
          "send_message_sequence"
        ],
        "conversationGuidance": "Prepara checkout cuando los facts requeridos esten completos. Antes de pago aprobado o reserva creada por herramienta, habla de solicitud, datos o link pendiente; no digas tu reserva, cambie tu reserva ni reserva confirmada. Si el cliente pide cambiar la hora o fecha sin dar el nuevo valor, pregunta a que hora o fecha actualizas la solicitud. Nunca construyas, copies ni reutilices un resumen o link del historial. Si el cliente pide resumen, link, anticipo o actualizar resumen, usa los facts actuales y llama prepare_checkout. Si el cliente cambia fecha u hora antes del pago, actualiza el fact con set_fact o con el resultado de check_availability, valida disponibilidad con servicio, fecha y hora actuales y despues llama prepare_checkout si ya habia resumen o link pendiente. Si cambia servicio o complementos antes del pago, actualiza primero el fact correspondiente; para quitar complementos registra add_ons=ninguno. Presenta como vigente solo el resumen o link devuelto por la herramienta. Si el checkout no requiere pago, pide confirmacion verbal del resumen y crea la reserva solo cuando customer_confirmed=true venga del ultimo mensaje del cliente."
      }
    ]
    },
    {
      "id": "reservation_management",
      "type": "secondary",
      "ttlSeconds": 900,
      "routingGuidance": "Use only when the customer clearly wants to manage an existing reservation: view it, confirm attendance, cancel it, or change its date, time, service or add-ons. Do not use it for an open booking request, a pending checkout summary or a pending payment link.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "reservation_management",
          "name": "Gestion de reserva existente",
          "goal": "Gestionar una reserva existente sin mezclarla con una solicitud nueva.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "allowedActions": [
            "get_customer_reservations",
            "manage_reservation",
            "escalate_to_human"
          ],
          "entryActions": [
            {
              "tool": "get_customer_reservations",
              "arguments": {}
            }
          ],
          "conversationGuidance": "Este flow solo aplica a reservas existentes. Si el cliente pide cambiar fecha u hora de una reserva existente, usa manage_reservation con el nuevo dato; la tool valida disponibilidad y aplica el cambio si corresponde. Si pide cambiar servicio o adicionales de una reserva ya confirmada, llama manage_reservation; la tool decide si coloca la reserva en espera y escala. Si hay varias reservas, usa get_customer_reservations o pide que la identifique por fecha, hora o servicio; nunca pidas UUID al cliente. No generes checkout nuevo para cambios de una reserva ya pagada. Si el cliente empieza una solicitud nueva, deja que el router vuelva al flow booking."
        }
      ]
    }
  ],
  "globalActions": [
    {
      "id": "human_escalation",
      "priority": 1000,
      "goal": "Escalar a una persona cuando el cliente lo pida, este inconforme, necesite cotizacion exacta de servicio variable o la solicitud salga del alcance del bot.",
      "allowedActions": [
        "escalate_to_human"
      ],
      "entryActions": [
        {
          "tool": "escalate_to_human",
          "arguments": {
            "reason": "customer_requested_human"
          },
          "when": {
            "messageMatches": [
              {
                "anyOf": [
                  "hablar con una persona",
                  "hablar con un humano",
                  "hablar con humano",
                  "hablar con asesor",
                  "quiero un asesor",
                  "necesito un asesor",
                  "necesito ayuda de una persona",
                  "quiero atencion humana",
                  "pasame con alguien",
                  "comunicarme con una persona"
                ]
              }
            ]
          }
        }
      ],
      "conversationGuidance": "Escala con resumen breve de la necesidad del cliente."
    }
  ],
  "factSchema": [
    {
      "key": "session.engagement",
      "role": "session.engagement",
      "label": "contexto de engagement",
      "type": "string",
      "required": false,
      "source": "session",
      "scope": "ephemeral",
      "expireOnBusinessDayChange": true
    },
    {
      "key": "booking_intent",
      "role": "booking.intent",
      "label": "intencion de reserva",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "expireOnBusinessDayChange": true,
      "aliases": [
        "reservar",
        "cita",
        "agenda",
        "servicio",
        "precio",
        "corte",
        "barba",
        "cejas",
        "lavado",
        "domicilio",
        "tratamiento"
      ]
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true,
      "aliases": [
        "servicio",
        "barba",
        "cejas",
        "lavado",
        "domicilio",
        "coloracion",
        "keratina",
        "tratamiento"
      ]
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
      "expireOnBusinessDayChange": true,
      "aliases": [
        "fecha",
        "dia",
        "cuando",
        "hoy",
        "manana"
      ]
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
      "expireOnBusinessDayChange": true,
      "aliases": [
        "hora",
        "horario"
      ]
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
      "expireOnBusinessDayChange": true,
      "aliases": [
        "direccion",
        "ubicacion",
        "barrio",
        "domicilio",
        "color",
        "keratina",
        "tratamiento",
        "nota"
      ]
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
      ],
      "aliases": [
        "adicional",
        "adicionales",
        "mascarilla",
        "aerografo",
        "sombreado",
        "fibra",
        "relajador",
        "masaje"
      ]
    },
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del cliente",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "nombre",
        "cliente",
        "a nombre de",
        "mi nombre"
      ]
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "telefono del cliente",
      "type": "phone",
      "required": true,
      "source": "channel",
      "scope": "customer",
      "aliases": [
        "telefono",
        "celular",
        "whatsapp",
        "numero"
      ]
    },
    {
      "key": "customer_birth_date",
      "role": "customer.birth_date",
      "label": "fecha de cumpleanos del cliente",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "cumpleanos",
        "fecha de cumpleanos",
        "fecha nacimiento",
        "fecha de nacimiento",
        "nacimiento"
      ]
    },
    {
      "key": "customer_email",
      "role": "customer.email",
      "label": "email del cliente",
      "type": "email",
      "required": false,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "email",
        "correo"
      ]
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
      ],
      "aliases": [
        "confirmo",
        "confirmo reserva",
        "si confirmo",
        "confirmado"
      ]
    }
  ],
  "guards": {
    "capability:checkout.prepare": {
      "requires": [
        "verification:availability_checked"
      ]
    },
    "capability:reservation.create": {
      "requires": [
        "verification:availability_checked",
        "verification:customer_identified",
        "verification:checkout_no_payment_prepared",
        "state:no_pending_checkout",
        "flag:verbal_confirmation"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "get_service_catalog",
    "get_compatible_add_ons",
    "resolve_service_selection",
    "check_availability",
    "prepare_checkout",
    "create_reservation",
    "get_customer_reservations",
    "manage_reservation",
    "verify_payment",
    "escalate_to_human",
    "reset_flow_context",
    "send_message_sequence"
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
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Luis',
         N'Agente de reservas de Luis Petit Profesional Barber con agenda por hora, anticipo del 100% y notificaciones de reserva.',
         1, @SettingsJson, @SystemPrompt, N'gpt-4.1-mini', 0.66, 8, GETUTCDATE());
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
        SystemPromptMarkdown = @SystemPrompt,
        Model = N'gpt-4.1-mini',
        Temperature = 0.66,
        MaxToolIterations = 8,
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


