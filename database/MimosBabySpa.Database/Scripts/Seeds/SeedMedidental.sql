-- =============================================================================
-- SeedMedidental.sql
--
-- Negocio Medidental con flujo de pedidos abierto, catalogo dental y recomendaciones comerciales,
-- recomendaciones controladas por catalogo y cierre de pedido.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @TenantId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000001';
DECLARE @BusinessId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000010';
DECLARE @AgentId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000020';
DECLARE @LocalCommerceConnectionId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000030';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;

SELECT TOP (1) @AgentTypeId = AgentTypeId
FROM dbo.AgentTypes
WHERE IsActive = 1
ORDER BY Name;

IF @AgentTypeId IS NULL
BEGIN
    PRINT N'SeedMedidental: AgentType activo no encontrado; omitiendo.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, [Name], Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'Medidental', N'admin@medidental.com', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET [Name] = N'Medidental',
        Email = N'admin@medidental.com',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, [Name], [Description], [Address], Phone, Email, Website, TimeZone, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'Medidental',
         N'Distribuidora de equipos, materiales y consumibles odontologicos para consultorios, clinicas, laboratorios y profesionales.',
         N'Valledupar, Cesar', N'+573000000000', N'admin@medidental.com', N'', N'America/Bogota', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        [Name] = N'Medidental',
        [Description] = N'Distribuidora de equipos, materiales y consumibles odontologicos para consultorios, clinicas, laboratorios y profesionales.',
        [Address] = COALESCE(NULLIF([Address], N''), N'Valledupar, Cesar'),
        Phone = COALESCE(NULLIF(Phone, N''), N'+573000000000'),
        Email = N'admin@medidental.com',
        Website = COALESCE(Website, N''),
        TimeZone = N'America/Bogota',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END

MERGE dbo.IntegrationConnections AS target
USING (
    SELECT
        @LocalCommerceConnectionId AS IntegrationConnectionId,
        @BusinessId AS BusinessId,
        CAST(1 AS INT) AS ConnectionType,
        CAST(0 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        N'Comercio local Medidental' AS [Name],
        N'local' AS AccountIdentifier,
        N'{"currency":"COP","manageStock":false}' AS SettingsJson,
        CAST(NULL AS NVARCHAR(MAX)) AS SecretsJson,
        CAST(1 AS BIT) AS IsEnabled
) AS source
   ON target.IntegrationConnectionId = source.IntegrationConnectionId
   OR (target.BusinessId = source.BusinessId
       AND target.ConnectionType = source.ConnectionType
       AND target.Provider = source.Provider
       AND target.Capability = source.Capability)
WHEN MATCHED THEN
    UPDATE SET
        ConnectionType = source.ConnectionType,
        Provider = source.Provider,
        Capability = source.Capability,
        [Name] = source.[Name],
        AccountIdentifier = source.AccountIdentifier,
        SettingsJson = source.SettingsJson,
        SecretsJson = COALESCE(target.SecretsJson, source.SecretsJson),
        IsEnabled = source.IsEnabled,
        LastError = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
            AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
    VALUES (source.IntegrationConnectionId, source.BusinessId, source.ConnectionType, source.Provider, source.Capability,
            source.[Name], source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());

DECLARE @Products TABLE
(
    ProductId UNIQUEIDENTIFIER NOT NULL,
    Sku NVARCHAR(100) NULL,
    [Name] NVARCHAR(250) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    CategoryName NVARCHAR(150) NULL,
    UnitPrice DECIMAL(18, 2) NOT NULL,
    Currency NVARCHAR(10) NOT NULL,
    StockQuantity DECIMAL(18, 2) NULL,
    IsActive BIT NOT NULL
);

INSERT INTO @Products
    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, IsActive)
VALUES
    ('D3E4A700-0000-0000-0000-000000000100', N'MD-TRIPOWER-DIGITAL', N'Electrobisturi 3G TRIPOWER Digital', N'Unidad electroquirurgica monopolar para odontologia, dermatologia y procedimientos menores. Modos CUT, CUT1, CUT2 y COAG; potencia maxima 50/45/40/40 W, frecuencia 600 kHz, entrada 115/230 Vac, pedal, pieza de mano y electrodo neutro. Compacto, 190 x 85 x 239 mm y 2,5 kg. Uso profesional, con puesta a tierra y accesorios compatibles.', N'Cirugia y electrocirugia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000101', N'MD-TRIPOWER-TACTIL', N'Electrobisturi 3G TRIPOWER Tactil', N'Electrobisturi monopolar con pantalla tactil, CUT, CUT1, CUT2 y COAG, autodiagnostico y actualizacion USB. Potencia maxima 50/45/40/40 W, 600 kHz, 115/230 Vac, 190 x 85 x 239 mm y 2,5 kg. Recomendado cuando se buscan ajustes visibles y operacion intuitiva en odontologia y procedimientos menores.', N'Cirugia y electrocirugia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000102', N'MD-TURBINA-TORCH-LED', N'Pieza de mano 3G Titanium Torch LED', N'Turbina de alto torque para jornadas intensivas y rehabilitacion, con titanio grado quirurgico, Push Button, triple irrigacion, rodamientos ceramicos y LED autogenerado de 3000-3500 mcd. Velocidad 320.000-420.000 rpm, cabezal alto torque, acople Borden y garantia de 1 ano.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000103', N'MD-TURBINA-TITANIUM-45P', N'Pieza de mano 3G Titanium 45P', N'Pieza de mano quirurgica con cabezal angulado a 45 grados, Push Button, luz LED, cuerpo de titanio, irrigacion y rodamientos ceramicos. Velocidad 320.000-420.000 rpm. Facilita el acceso a zonas posteriores, terceros molares, cirugia y odontopediatria; incluye llave y garantia de 1 ano.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000104', N'MD-MICROMOTOR-PUNTA-RECTA', N'Micromotor 3G con punta recta', N'Conjunto neumatico de baja velocidad con irrigacion externa, giro suave y cambio adelante-atras. Micromotor hasta 25.000 rpm y punta recta hasta 20.000 rpm; acople Borden, presion recomendada 33 PSI y garantia de 1 ano. Recomendado para ajustes, acabados y procedimientos de baja velocidad.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000105', N'MD-SCALER-P6-MAX', N'Escaler ultrasonico 3G P6 Max con LED', N'Piezo scaler con pieza de mano optica LED desmontable y esterilizable hasta 135 C. Incluye unidad, adaptador, pedal, tubo de agua, llave de torque y cinco puntas. Indicado para calculo supragingival, desbridamiento subgingival, biopeliculas, endotoxinas y cemento de ortodoncia.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000106', N'MD-MAGPOWER-ULTRASONICO', N'Escaler ultrasonico 3G MagPower', N'Equipo multifuncional para limpieza general, periodoncia e irrigacion de conductos. Mango desmontable autoclavable a 135 C, frecuencia 30 kHz, ocho niveles de potencia, control de agua y Turbo Boost para calculo pesado; compatible opcionalmente con puntas EMS o Satelec. Alimentacion 110/240 Vac segun configuracion.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000107', N'MD-POWERLED-L9', N'Lampara de fotocurado 3G PowerLED L9', N'Lampara de amplio espectro 385-520 nm, intensidad normal 1600 mW/cm2 y Super Strong hasta 2500 mW/cm2. Curado normal 5-20 s o alto 1-3 s, bateria recargable con 4-6 h de autonomia y entrada 100-240 Vac. Ideal para brackets, carillas y restauraciones esteticas.', N'Fotocurado', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000108', N'MD-ETCHANT-GEL-37', N'Etchant Gel Smile Designers 37%', N'Gel de acido ortofosforico al 37% para grabado de esmalte y dentina. Alta tixotropia para que permanezca en el area, colocacion precisa y lavado facil. Util en composites, ceramicas, coronas, carillas, puentes, ferulas, brackets y selladores; presentaciones individuales y kits con jeringas y puntas. Uso profesional.', N'Adhesivos y grabado', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000109', N'MD-LC-BOND-6ML', N'LC Bond adhesivo universal 5a generacion', N'Adhesivo monocomponente fotocurable e hidrofilico para esmalte y dentina, apto para tecnica de union en humedo. Formula con PMGDM, acetona y agua destilada; aplicacion en 2-3 capas, pelicula aproximada de 5 micras y fotocurado indicado de 20 s. Presentacion de 6 ml, complemento del grabado antes de composites y compomeros.', N'Adhesivos y grabado', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000110', N'MD-BISG-MA', N'Bisg-Ma resina nanohibrida', N'Resina nanohibrida fotocurable de 4 g para restauraciones anteriores, posteriores y carillas. Facil de modelar y pulir, con acabado natural, baja contraccion y radiopacidad; disponible en varios tonos segun catalogo. Buena eleccion para restauraciones esteticas donde importan brillo y versatilidad.', N'Resinas restauradoras', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000111', N'MD-TEGD-MA', N'Tegd-Ma resina microhibrida', N'Resina en pasta microhibrida fotocurable de 4 g, facil de manipular, con excelente pulido, alto brillo y resistencia al desgaste y manchas. Tonos A1, A2, A3, A3.5, B1, B2, B3, B4, C1, C2 y C3 segun catalogo; radiopaca, baja contraccion y curado orientativo 30 s en tonos claros y 40 s en oscuros.', N'Resinas restauradoras', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000112', N'MD-SYGD-MA', N'Sygd-Ma resina fluida', N'Resina fluida fotocurable de baja viscosidad y alta estetica, con particula aproximada de 0,7 micras. Se adapta a cavidades, zonas cervicales, pequenos defectos, sellado de fosas y fisuras y como base bajo composites. Jeringa de 2 g, buen pulido, baja contraccion y radiopacidad.', N'Resinas restauradoras', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000113', N'MD-AVVA-BULK', N'Avva-Bulk resina fluida Bulk', N'Resina fluida fotocurable para restauraciones y cementacion de coronas, puentes y ceramicas. Alta translucidez para facilitar el curado y capas de hasta 4 mm segun protocolo; alta carga, resistencia flexural y tono dental universal. Presentacion de jeringa de 2 g o caja con 20 puntas.', N'Resinas restauradoras', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000114', N'MD-DV-SEAL', N'DV-Seal sellante de fosas y fisuras', N'Sellador fotocurable a base de BIS-GMA, tixotropico para penetrar fisuras estrechas, con adhesion al esmalte y resistencia al desgaste. El color rosa cambia a blanco opaco para ayudar a verificar la polimerizacion y libera fluor. Presentaciones con jeringas, grabador y puntas; indicado para superficies oclusales y prevencion de caries.', N'Selladores', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000115', N'MD-LUMA-FLEX', N'Kit de resina para ortodoncia Luma-Flex', N'Kit fotocurable para cementacion de brackets de metal, porcelana o plastico. Pasta de alta viscosidad para capas delgadas, sin mezcla, fotocurado 20-30 s, resistencia a compresion 305 MPa, flexion 142 MPa y cizallamiento 22 MPa. Incluye dos jeringas de pasta de 5 g, enlace de 5 ml, gel grabador de 2 ml y accesorios.', N'Ortodoncia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000116', N'MD-SILICA-FLEX', N'Kit de resina para ortodoncia Silica-Flex autocurado', N'Kit de autocurado para brackets de metal, porcelana o plastico. Incluye adhesivo de 5 ml, gel grabador y pasta; la polimerizacion inicia al contacto de imprimacion y pasta. Requiere esmalte limpio, seco y aislado; incluye microaplicadores y boquillas.', N'Ortodoncia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000117', N'MD-IONO-CEM-TIPO-I', N'Iono-Cem ionomero cementante Tipo I', N'Cemento de ionomero de vidrio para coronas, puentes, inlays, onlays y ceramicas. Adhesion quimica a esmalte, dentina y metal, liberacion de fluor y pelicula aproximada de 15 micras. Presentacion de 20 g de polvo y 15 ml de liquido con dosificador y base de mezcla.', N'Ionomeros', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000118', N'MD-IONO-RES-TIPO-II', N'Iono-Res ionomero restaurador Tipo II', N'Ionome​ro de vidrio para restauraciones Clase III y V, erosiones cervicales, superficies radiculares, restauraciones pediatricas y tecnica sandwich. Adhesion a esmalte y dentina, liberacion continua de fluor, resistencia a abrasion, baja solubilidad y tonos 21 y 22. Presentacion de 20 g de polvo y 10 ml de liquido.', N'Ionomeros', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000119', N'MD-OSSEO-100', N'Motor de implantes 3G Osseo 100', N'Motor BLDC para implantologia con autocalibracion, 9 memorias, proteccion contra sobrecarga, pantalla tactil, pedal y pieza de mano/contraangulo 20:1 esterilizable. Alcanza 2.500 rpm y 55 N.cm con relacion 20:1; admite relaciones 1:5, 1:4, 1:1, 16:1, 20:1, 27:1, 32:1 y 64:1. Muestra condiciones reales de trabajo.', N'Implantologia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000120', N'MD-OSSEO-200', N'Motor de implantes 3G Osseo 200', N'Motor BLDC avanzado con torque y rpm en tiempo real, autocalibracion, 9 memorias, diagnostico inteligente, proteccion contra sobrecarga y controlador de pie. Admite varias relaciones; con contraangulo 20:1 trabaja de 15 a 2.000 rpm y hasta 70 N.cm. Disponible con funcion optica segun configuracion.', N'Implantologia', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000121', N'MD-ELECTRODOS-TRIPOWER', N'Kit de electrodos para electrocirugia', N'Accesorios compatibles con TRI-POWER: electrodos rectos, de bola, aguja, bucle, gancho y lamina, en distintas longitudes; kits surtidos de 5 o 10 piezas, electrodos neutros, cables y esponja limpiadora. Confirmar referencia segun equipo, longitud y tecnica.', N'Accesorios de electrocirugia', 0, N'COP', NULL, 1);


INSERT INTO @Products
    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, IsActive)
VALUES
    ('D3E4A700-0000-0000-0000-000000000122', N'MD-CHRO-MA', N'Chro-Ma resina nanohibrida de alta estetica', N'Resina fotocurable NanoFill de 4 g, especializada para carillas y restauraciones anteriores y posteriores de alta estetica. Facil de modelar y pulir, radiopaca, de baja contraccion y con acabado de alto brillo.', N'Resinas restauradoras', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000123', N'MD-KIT-SYGD-MA', N'Kit de resinas para restauracion SYGD-MA', N'Kit con tres jeringas de 2 g de resina fluida SYGD-MA, gel desmineralizante de 2,5 ml, accesorios e instrucciones. Resina fotocurable de baja viscosidad para preparaciones de cavidades y restauraciones esteticas.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000124', N'MD-KIT-BISG-MA', N'Kit de resinas para restauracion BISG-MA', N'Kit con cinco jeringas de 4 g de resina nanohibrida BISG-MA, adhesivo de 5 ml, accesorios e instrucciones. Indicado para restauraciones anteriores, posteriores y esteticas.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000125', N'MD-KIT-CHRO-MA', N'Kit de resinas para restauracion CHRO-MA', N'Kit con cinco jeringas de 4 g de resina NanoFill CHRO-MA, adhesivo de 5 ml, accesorios e instrucciones. Especializado para carillas y restauraciones de alta estetica.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000126', N'MD-KIT-TEGD-MA', N'Kit de resinas para restauracion TEGD-MA', N'Kit con cinco jeringas de 4 g de resina microhibrida TEGD-MA, adhesivo de 5 ml, accesorios e instrucciones. Ofrece variedad de tonos, resistencia y alto pulido.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000127', N'MD-KIT-AVVA-BULK', N'Kit de resina fluida Bulk AVVA-BULK', N'Kit de resina fluida Bulk para restauraciones profundas y cementacion, con tres jeringas de 2 g, una jeringa de 2 g, gel grabador de 2,5 ml y puntas plasticas, segun presentacion de catalogo.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000128', N'MD-KIT-DV-SEAL', N'Kit sellante de fosas y fisuras DV-SEAL', N'Kit de sellante fotocurable DV-SEAL con jeringas, gel grabador de 2,5 ml y puntas plasticas. Resina BIS-GMA tixotropica con liberacion de fluor y cambio de color para verificar el curado.', N'Kits de restauracion', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000129', N'MD-COMPULA-SYGD-MA', N'Compulas de resina fluida SYGD-MA caja x20', N'Caja con 20 compulas de 0,28 g de resina fluida SYGD-MA fotocurable, de baja viscosidad, alta estetica y buen pulido para restauraciones y zonas de dificil acceso.', N'Resinas en compulas', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000130', N'MD-COMPULA-TEGD-MA', N'Compulas de resina microhibrida TEGD-MA caja x20', N'Caja con 20 compulas de 0,28 g de resina microhibrida TEGD-MA fotocurable, de facil manejo, alto pulido y buena resistencia para restauraciones.', N'Resinas en compulas', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000131', N'MD-COMPULA-BISG-MA', N'Compulas de resina nanohibrida BISG-MA caja x20', N'Caja con 20 compulas de 0,28 g de resina nanohibrida BISG-MA fotocurable para restauraciones anteriores, posteriores y esteticas.', N'Resinas en compulas', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000132', N'MD-COMPULA-CHRO-MA', N'Compulas de resina CHRO-MA caja x20', N'Caja con 20 compulas de 0,28 g de resina CHRO-MA NanoFill fotocurable, especializada para carillas y restauraciones de alta estetica.', N'Resinas en compulas', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000133', N'MD-COMPULA-AVVA-BULK', N'Compulas de resina Bulk AVVA-BULK caja x20', N'Caja con 20 compulas o puntas de 0,28 g de resina fluida Bulk AVVA-BULK, de alta translucidez para capas profundas y cementacion.', N'Resinas en compulas', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000134', N'MD-AUTOCURADO-3-15', N'Kit de resina de autocurado 3 g y 15 g', N'Composite restaurador radiopaco de curado quimico para procedimientos anteriores y posteriores. Disponible en presentaciones de 3 g y 15 g, con base, catalizador, adhesivo, acido grabador, mezcladores y base de mezcla.', N'Resinas de autocurado', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000135', N'MD-CERAMIK-TRADICIONAL', N'Pieza de mano 3G Ceramik Tradicional', N'Pieza de mano de alta velocidad con cuerpo ergonomico de acero inoxidable, cabezal estandar, sistema tradicional cambia fresa y rodamientos ceramicos.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000136', N'MD-CERAMIK-MINI', N'Pieza de mano 3G Ceramik Tradicional Mini', N'Pieza de mano de alta velocidad con cuerpo de acero inoxidable, cabezal mini para acceso reducido y odontopediatria, sistema tradicional cambia fresa y rodamientos ceramicos.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000137', N'MD-TITANIUM-3P', N'Pieza de mano 3G Titanium 3P', N'Pieza de mano de alta velocidad con recubrimiento de titanio grado quirurgico, sistema Push Button, triple irrigacion y sistema antirretraccion.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000138', N'MD-TITANIUM-3R', N'Pieza de mano 3G Titanium 3R', N'Pieza de mano de alta velocidad con recubrimiento de titanio grado quirurgico, cabezal estandar, sistema tradicional cambia fresa, triple irrigacion y antirretraccion.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000139', N'MD-TITANIUM-45R', N'Pieza de mano 3G Titanium LED 45R', N'Pieza de mano quirurgica de alta velocidad con cabezal a 45 grados, luz LED, sistema tradicional cambia fresa y acople Borden; facilita acceso posterior y cirugia.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000140', N'MD-MICROMOTOR-SET-RECTA', N'Micromotor Set 3G con punta recta', N'Set clasico de baja velocidad con micromotor neumatico, irrigacion externa y cono de punta recta. Permite giro suave y cambio de direccion para ajustes y acabados.', N'Piezas de mano', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000141', N'MD-CABEZA-CONTRAANGULO-PB', N'Cabeza de contraangulo 3G Push Button', N'Cabeza intercambiable para contraangulo 3G Ceramik, compatible con fresas CA, sistema Push Button y velocidad maxima aproximada de 40.000 rpm.', N'Contraangulos y accesorios', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000142', N'MD-CABEZA-CONTRAANGULO-PESTILLO', N'Cabeza de contraangulo 3G de pestillo', N'Cabeza intercambiable para contraangulo 3G Ceramik, compatible con fresas CA, sistema de pestillo y velocidad maxima aproximada de 40.000 rpm.', N'Contraangulos y accesorios', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000143', N'MD-CONTRAANGULO-PB', N'Contraangulo 3G Ceramik Push Button', N'Contraangulo completo 3G Ceramik tipo E 1:1 con sistema Push Button, funcionamiento silencioso y construccion durable para procedimientos de baja velocidad.', N'Contraangulos y accesorios', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000144', N'MD-CONTRAANGULO-PESTILLO', N'Contraangulo 3G Ceramik de pestillo', N'Contraangulo completo 3G Ceramik tipo E 1:1 con sistema tradicional de pestillo, funcionamiento silencioso y construccion durable para procedimientos de baja velocidad.', N'Contraangulos y accesorios', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000145', N'MD-SCALER-BLACK', N'Escaler neumatico 3G Black Edition', N'Escaler neumatico portatil y minimamente invasivo para remover placa y sarro subgingival y supragingival. Compatible con NSK e incluye cinco puntas.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000146', N'MD-SCALER-AS6000', N'Escaler neumatico 3G AS6000 Silver Edition', N'Escaler neumatico portatil para remover placa y depositos de sarro subgingival y supragingival. Compatible con NSK e incluye tres puntas.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000147', N'MD-SCALER-P5-MAX', N'Escaler ultrasonico 3G P5 Max sin LED', N'Piezo escaler ultrasonico no optico con pieza de mano, control de agua, pedal, adaptador y cinco puntas. Indicado para profilaxis, calculo y trabajo periodontal.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000148', N'MD-CAVITRON-MAGPOWER', N'Cavitron magnetostrictivo 3G MagPower', N'Cavitron magnetostrictivo de doble frecuencia para odontologia, periodoncia, irrigacion y limpieza de conductos. Ofrece trabajo suave, velocidades constantes y manejo practico.', N'Ultrasonido e higiene', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000149', N'MD-POWERLED-L7', N'Lampara de fotocurado 3G PowerLED L7', N'Lampara LED de alto rendimiento para polimerizaciones dentales en consultorio, con ajustes de tiempo y diseno orientado al control del fotocurado.', N'Fotocurado', 0, N'COP', NULL, 1),
    ('D3E4A700-0000-0000-0000-000000000150', N'MD-POWERLED-LX', N'Lampara de fotocurado 3G PowerLED LX', N'Lampara LED de fotocurado y aceleracion de blanqueamiento, intensidad de 1200 a 1400 mW/cm2, longitud de onda de 420 a 480 nm y tiempos configurables de 5 a 40 segundos.', N'Fotocurado', 0, N'COP', NULL, 1);
MERGE dbo.Products AS target
USING @Products AS source
   ON target.BusinessId = @BusinessId
  AND target.Sku = source.Sku
WHEN MATCHED THEN
    UPDATE SET
        IntegrationConnectionId = @LocalCommerceConnectionId,
        ExternalProductId = NULL,
        Source = 0,
        [Name] = source.[Name],
        [Description] = source.[Description],
        CategoryName = source.CategoryName,
        UnitPrice = source.UnitPrice,
        Currency = source.Currency,
        ManageStock = 0,
        StockQuantity = source.StockQuantity,
        IsActive = source.IsActive,
        RawPayloadJson = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ProductId, BusinessId, IntegrationConnectionId, ExternalProductId, Source, Sku, [Name],
            [Description], CategoryName, UnitPrice, Currency, ManageStock, StockQuantity,
            IsActive, RawPayloadJson, LastSyncedAt, CreatedAt)
    VALUES (source.ProductId, @BusinessId, @LocalCommerceConnectionId, NULL, 0, source.Sku, source.[Name],
            source.[Description], source.CategoryName, source.UnitPrice, source.Currency, 0, source.StockQuantity,
            source.IsActive, NULL, NULL, GETUTCDATE());

DELETE FROM dbo.Products
WHERE BusinessId = @BusinessId
  AND Source = 0
  AND NOT EXISTS (
      SELECT 1
      FROM @Products p
      WHERE p.Sku = dbo.Products.Sku
  );


-- Rebuild the local lexical index from catalog identity, descriptions, and
-- curated PDF vocabulary. The search engine always receives normalized,
-- token-level terms; aliases below own multi-word product equivalences.
DECLARE @ProductVocabulary TABLE
(
    Sku NVARCHAR(100) NOT NULL,
    Keywords NVARCHAR(MAX) NOT NULL
);

INSERT INTO @ProductVocabulary (Sku, Keywords)
VALUES
    (N'MD-TRIPOWER-DIGITAL', N'electrobisturi electrocirugia cauterio monopolar corte coagulacion digital tripower'),
    (N'MD-TRIPOWER-TACTIL', N'electrobisturi electrocirugia cauterio monopolar corte coagulacion tactil tripower touchscreen'),
    (N'MD-TURBINA-TORCH-LED', N'turbina pieza mano titanium torch led torque rehabilitacion'),
    (N'MD-TURBINA-TITANIUM-45P', N'turbina pieza mano titanium led 45p quirurgica posterior tercer molar'),
    (N'MD-MICROMOTOR-PUNTA-RECTA', N'micromotor baja velocidad punta recta irrigacion borden'),
    (N'MD-SCALER-P6-MAX', N'escaler scaler piezo ultrasonico optico p6 max led profilaxis calculo sarro periodontal'),
    (N'MD-MAGPOWER-ULTRASONICO', N'escaler scaler ultrasonico magpower periodoncia endodoncia conducto calculo sarro'),
    (N'MD-POWERLED-L9', N'lampara fotocurado polimerizacion powerled l9 brackets carillas restauracion'),
    (N'MD-ETCHANT-GEL-37', N'desmineralizante grabador acido fosforico ortofosforico etchant gel esmalte dentina'),
    (N'MD-LC-BOND-6ML', N'adhesivo bond quinta generacion universal esmalte dentina fotocurable'),
    (N'MD-BISG-MA', N'resina bisg ma nanohibrida composite restauradora carilla fotocurable'),
    (N'MD-TEGD-MA', N'resina tegd ma teg ma microhibrida composite restauradora fotocurable'),
    (N'MD-SYGD-MA', N'resina sygd ma syg ma fluida composite restauradora cavidad cervical fotocurable'),
    (N'MD-AVVA-BULK', N'resina avva bulk ava bulk fluida profunda cementacion corona puente ceramica'),
    (N'MD-DV-SEAL', N'sellante dv seal fosa fisura pit fissure caries fluor fotocurable'),
    (N'MD-LUMA-FLEX', N'ortodoncia luma flex bracket fotocurado cementacion adhesivo'),
    (N'MD-SILICA-FLEX', N'ortodoncia silica flex bracket autocurado cementacion adhesivo'),
    (N'MD-IONO-CEM-TIPO-I', N'ionomero iono cem cementante tipo uno corona puente inlay onlay vidrio'),
    (N'MD-IONO-RES-TIPO-II', N'ionomero iono res restaurador tipo dos clase cinco pediatrica vidrio'),
    (N'MD-OSSEO-100', N'motor implante implantologia osseo cien xcub torque'),
    (N'MD-OSSEO-200', N'motor implante implantologia osseo doscientos bldc torque'),
    (N'MD-ELECTRODOS-TRIPOWER', N'electrodo electrocirugia tripower aguja bola bucle gancho lamina'),
    (N'MD-CHRO-MA', N'resina chro ma nanohibrida nanofill composite restauradora carilla estetica'),
    (N'MD-KIT-SYGD-MA', N'kit restauracion resina sygd ma fluida desmineralizante'),
    (N'MD-KIT-BISG-MA', N'kit restauracion resina bisg ma nanohibrida adhesivo'),
    (N'MD-KIT-CHRO-MA', N'kit restauracion resina chro ma nanofill carilla adhesivo'),
    (N'MD-KIT-TEGD-MA', N'kit restauracion resina tegd ma microhibrida adhesivo'),
    (N'MD-KIT-AVVA-BULK', N'kit restauracion resina avva bulk fluida profunda grabador'),
    (N'MD-KIT-DV-SEAL', N'kit sellante dv seal fosa fisura grabador fluor'),
    (N'MD-COMPULA-SYGD-MA', N'compula resina sygd ma fluida caja veinte'),
    (N'MD-COMPULA-TEGD-MA', N'compula resina tegd ma microhibrida caja veinte'),
    (N'MD-COMPULA-BISG-MA', N'compula resina bisg ma nanohibrida caja veinte'),
    (N'MD-COMPULA-CHRO-MA', N'compula resina chro ma nanofill carilla caja veinte'),
    (N'MD-COMPULA-AVVA-BULK', N'compula punta resina avva bulk fluida caja veinte'),
    (N'MD-AUTOCURADO-3-15', N'kit resina autocurado composite curado quimico restauracion base catalizador'),
    (N'MD-CERAMIK-TRADICIONAL', N'turbina pieza mano ceramik tradicional alta velocidad cabezal estandar'),
    (N'MD-CERAMIK-MINI', N'turbina pieza mano ceramik mini alta velocidad odontopediatria'),
    (N'MD-TITANIUM-3P', N'turbina pieza mano titanium tres p push button triple irrigacion'),
    (N'MD-TITANIUM-3R', N'turbina pieza mano titanium tres r tradicional triple irrigacion'),
    (N'MD-TITANIUM-45R', N'turbina pieza mano titanium led 45r quirurgica posterior'),
    (N'MD-MICROMOTOR-SET-RECTA', N'set micromotor baja velocidad punta recta irrigacion'),
    (N'MD-CABEZA-CONTRAANGULO-PB', N'cabeza contraangulo push button fresa ca cuarenta mil rpm'),
    (N'MD-CABEZA-CONTRAANGULO-PESTILLO', N'cabeza contraangulo pestillo fresa ca cuarenta mil rpm'),
    (N'MD-CONTRAANGULO-PB', N'contraangulo ceramik push button tipo e baja velocidad'),
    (N'MD-CONTRAANGULO-PESTILLO', N'contraangulo ceramik pestillo tipo e baja velocidad'),
    (N'MD-SCALER-BLACK', N'escaler scaler neumatico black edition placa sarro subgingival supragingival nsk'),
    (N'MD-SCALER-AS6000', N'escaler scaler neumatico as6000 silver edition placa sarro nsk'),
    (N'MD-SCALER-P5-MAX', N'escaler scaler piezo ultrasonico no optico p5 max profilaxis calculo sarro periodontal'),
    (N'MD-CAVITRON-MAGPOWER', N'cavitron magnetostrictivo magpower ultrasonico periodoncia endodoncia conducto'),
    (N'MD-POWERLED-L7', N'lampara fotocurado polimerizacion powerled l7 restauracion'),
    (N'MD-POWERLED-LX', N'lampara fotocurado polimerizacion powerled lx blanqueamiento');

DELETE FROM dbo.ProductSearchTerms
WHERE BusinessId = @BusinessId;

;WITH CatalogText AS
(
    SELECT
        p.ProductId,
        LOWER(
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                CONCAT(p.Sku, N' ', p.[Name], N' ', p.CategoryName, N' ', p.[Description], N' ', v.Keywords),
                N'-', N' '), N'/', N' '), N',', N' '), N'.', N' '),
                N'(', N' '), N')', N' '), N':', N' ')
        ) AS SearchText
    FROM dbo.Products p
    INNER JOIN @ProductVocabulary v ON v.Sku = p.Sku
    WHERE p.BusinessId = @BusinessId
),
NormalizedTerms AS
(
    SELECT DISTINCT
        @BusinessId AS BusinessId,
        c.ProductId,
        CONVERT(NVARCHAR(100), LTRIM(RTRIM(tokens.[value]))) AS Term
    FROM CatalogText c
    CROSS APPLY STRING_SPLIT(c.SearchText, N' ') tokens
    WHERE LEN(LTRIM(RTRIM(tokens.[value]))) BETWEEN 2 AND 100
      AND LTRIM(RTRIM(tokens.[value])) NOT IN
          (N'al', N'con', N'de', N'del', N'el', N'en', N'la', N'las', N'los',
           N'para', N'por', N'una', N'uno', N'unos', N'unas', N'que')
)
INSERT INTO dbo.ProductSearchTerms (BusinessId, ProductId, Term, CreatedAt)
SELECT BusinessId, ProductId, Term, GETUTCDATE()
FROM NormalizedTerms;

DECLARE @AliasDefinitions TABLE
(
    Sku NVARCHAR(100) NOT NULL,
    Alias NVARCHAR(250) NOT NULL,
    Kind INT NOT NULL,
    ResolutionMode INT NOT NULL
);

-- Exact model/synonym equivalences: active business AutoResolve.
INSERT INTO @AliasDefinitions (Sku, Alias, Kind, ResolutionMode)
VALUES
    (N'MD-TRIPOWER-DIGITAL', N'tripower digital', 0, 1),
    (N'MD-TRIPOWER-DIGITAL', N'electrobisturi digital', 0, 1),
    (N'MD-TRIPOWER-TACTIL', N'tripower tactil', 0, 1),
    (N'MD-TRIPOWER-TACTIL', N'electrobisturi tactil', 0, 1),
    (N'MD-TURBINA-TORCH-LED', N'titanium torch led', 0, 1),
    (N'MD-TURBINA-TITANIUM-45P', N'titanium 45p', 0, 0),
    (N'MD-SCALER-P6-MAX', N'p6 max', 0, 1),
    (N'MD-MAGPOWER-ULTRASONICO', N'escaler magpower', 0, 1),
    (N'MD-POWERLED-L9', N'powerled l9', 0, 1),
    (N'MD-ETCHANT-GEL-37', N'desmineralizante', 0, 1),
    (N'MD-ETCHANT-GEL-37', N'acido grabador 37', 0, 1),
    (N'MD-LC-BOND-6ML', N'lc bond', 0, 1),
    (N'MD-BISG-MA', N'resina bisg ma', 0, 1),
    (N'MD-TEGD-MA', N'resina teg ma', 2, 1),
    (N'MD-TEGD-MA', N'resina tegd ma', 0, 1),
    (N'MD-SYGD-MA', N'resina syg ma', 2, 1),
    (N'MD-SYGD-MA', N'resina sygd ma', 0, 1),
    (N'MD-AVVA-BULK', N'resina ava bulk', 2, 1),
    (N'MD-AVVA-BULK', N'resina avva bulk', 0, 1),
    (N'MD-DV-SEAL', N'dv seal', 0, 1),
    (N'MD-LUMA-FLEX', N'luma flex', 0, 1),
    (N'MD-SILICA-FLEX', N'silica flex', 0, 1),
    (N'MD-IONO-CEM-TIPO-I', N'iono cem', 0, 1),
    (N'MD-IONO-RES-TIPO-II', N'iono res', 0, 1),
    (N'MD-OSSEO-100', N'osseo 100', 0, 1),
    (N'MD-OSSEO-200', N'osseo 200', 0, 1),
    (N'MD-ELECTRODOS-TRIPOWER', N'electrodo tripower', 0, 1),
    (N'MD-CHRO-MA', N'resina chro ma', 0, 1),
    (N'MD-KIT-SYGD-MA', N'kit sygd ma', 0, 1),
    (N'MD-KIT-BISG-MA', N'kit bisg ma', 0, 1),
    (N'MD-KIT-CHRO-MA', N'kit chro ma', 0, 1),
    (N'MD-KIT-TEGD-MA', N'kit tegd ma', 0, 1),
    (N'MD-KIT-AVVA-BULK', N'kit avva bulk', 0, 1),
    (N'MD-KIT-DV-SEAL', N'kit dv seal', 0, 1),
    (N'MD-COMPULA-SYGD-MA', N'compula sygd ma', 0, 1),
    (N'MD-COMPULA-TEGD-MA', N'compula tegd ma', 0, 1),
    (N'MD-COMPULA-BISG-MA', N'compula bisg ma', 0, 1),
    (N'MD-COMPULA-CHRO-MA', N'compula chro ma', 0, 1),
    (N'MD-COMPULA-AVVA-BULK', N'compula avva bulk', 0, 1),
    (N'MD-AUTOCURADO-3-15', N'resina autocurado 3 15', 0, 1),
    (N'MD-CERAMIK-TRADICIONAL', N'ceramik tradicional', 0, 1),
    (N'MD-CERAMIK-MINI', N'ceramik mini', 0, 1),
    (N'MD-TITANIUM-3P', N'titanium 3p', 0, 0),
    (N'MD-TITANIUM-3R', N'titanium 3r', 0, 0),
    (N'MD-TITANIUM-45R', N'titanium 45r', 0, 0),
    (N'MD-CABEZA-CONTRAANGULO-PB', N'cabeza contraangulo push button', 0, 1),
    (N'MD-CABEZA-CONTRAANGULO-PESTILLO', N'cabeza contraangulo pestillo', 0, 1),
    (N'MD-CONTRAANGULO-PB', N'contraangulo push button', 0, 1),
    (N'MD-CONTRAANGULO-PESTILLO', N'contraangulo pestillo', 0, 1),
    (N'MD-SCALER-BLACK', N'escaler black edition', 0, 1),
    (N'MD-SCALER-AS6000', N'escaler as6000', 0, 1),
    (N'MD-SCALER-P5-MAX', N'p5 max', 0, 1),
    (N'MD-CAVITRON-MAGPOWER', N'cavitron magpower', 0, 1),
    (N'MD-POWERLED-L7', N'powerled l7', 0, 1),
    (N'MD-POWERLED-LX', N'powerled lx', 0, 1);

-- Category/use expressions are intentionally SuggestOnly for every valid candidate.
INSERT INTO @AliasDefinitions (Sku, Alias, Kind, ResolutionMode)
VALUES
    (N'MD-TRIPOWER-DIGITAL', N'electrobisturi', 1, 0),
    (N'MD-TRIPOWER-TACTIL', N'electrobisturi', 1, 0),
    (N'MD-OSSEO-100', N'motor implante', 1, 0),
    (N'MD-OSSEO-200', N'motor implante', 1, 0),
    (N'MD-IONO-CEM-TIPO-I', N'ionomero', 1, 0),
    (N'MD-IONO-RES-TIPO-II', N'ionomero', 1, 0),
    (N'MD-SYGD-MA', N'resina fluida', 1, 0),
    (N'MD-AVVA-BULK', N'resina fluida', 1, 0),
    (N'MD-BISG-MA', N'resina nanohibrida', 1, 0),
    (N'MD-CHRO-MA', N'resina nanohibrida', 1, 0),
    (N'MD-KIT-SYGD-MA', N'kit restauracion', 1, 0),
    (N'MD-KIT-BISG-MA', N'kit restauracion', 1, 0),
    (N'MD-KIT-CHRO-MA', N'kit restauracion', 1, 0),
    (N'MD-KIT-TEGD-MA', N'kit restauracion', 1, 0),
    (N'MD-KIT-AVVA-BULK', N'kit restauracion', 1, 0),
    (N'MD-KIT-DV-SEAL', N'kit restauracion', 1, 0),
    (N'MD-COMPULA-SYGD-MA', N'compula resina', 1, 0),
    (N'MD-COMPULA-TEGD-MA', N'compula resina', 1, 0),
    (N'MD-COMPULA-BISG-MA', N'compula resina', 1, 0),
    (N'MD-COMPULA-CHRO-MA', N'compula resina', 1, 0),
    (N'MD-COMPULA-AVVA-BULK', N'compula resina', 1, 0),
    (N'MD-TURBINA-TORCH-LED', N'pieza mano', 1, 0),
    (N'MD-TURBINA-TITANIUM-45P', N'pieza mano', 1, 0),
    (N'MD-CERAMIK-TRADICIONAL', N'pieza mano', 1, 0),
    (N'MD-CERAMIK-MINI', N'pieza mano', 1, 0),
    (N'MD-TITANIUM-3P', N'pieza mano', 1, 0),
    (N'MD-TITANIUM-3R', N'pieza mano', 1, 0),
    (N'MD-TITANIUM-45R', N'pieza mano', 1, 0),
    (N'MD-CABEZA-CONTRAANGULO-PB', N'contraangulo', 1, 0),
    (N'MD-CABEZA-CONTRAANGULO-PESTILLO', N'contraangulo', 1, 0),
    (N'MD-CONTRAANGULO-PB', N'contraangulo', 1, 0),
    (N'MD-CONTRAANGULO-PESTILLO', N'contraangulo', 1, 0),
    (N'MD-SCALER-BLACK', N'escaler', 1, 0),
    (N'MD-SCALER-AS6000', N'escaler', 1, 0),
    (N'MD-SCALER-P5-MAX', N'escaler', 1, 0),
    (N'MD-SCALER-P6-MAX', N'escaler', 1, 0),
    (N'MD-MAGPOWER-ULTRASONICO', N'escaler', 1, 0),
    (N'MD-POWERLED-L7', N'lampara fotocurado', 1, 0),
    (N'MD-POWERLED-L9', N'lampara fotocurado', 1, 0),
    (N'MD-POWERLED-LX', N'lampara fotocurado', 1, 0);

-- ProductSearchText.NormalizeAlias splits alpha/numeric transitions and drops
-- one-letter alpha tokens. Keep persisted keys aligned with runtime lookup.
DECLARE @AliasNormalization TABLE
(
    Alias NVARCHAR(250) NOT NULL PRIMARY KEY,
    NormalizedAlias NVARCHAR(250) NOT NULL
);

INSERT INTO @AliasNormalization (Alias, NormalizedAlias)
VALUES
    (N'titanium 45p', N'titanium 45'),
    (N'p6 max', N'6 max'),
    (N'powerled l9', N'powerled 9'),
    (N'titanium 3p', N'titanium 3'),
    (N'titanium 3r', N'titanium 3'),
    (N'titanium 45r', N'titanium 45'),
    (N'escaler as6000', N'escaler as 6000'),
    (N'p5 max', N'5 max'),
    (N'powerled l7', N'powerled 7');

;WITH AliasSource AS
(
    SELECT
        p.ProductId,
        d.Alias,
        COALESCE(n.NormalizedAlias, d.Alias) AS NormalizedAlias,
        d.Kind,
        d.ResolutionMode
    FROM @AliasDefinitions d
    INNER JOIN dbo.Products p
        ON p.BusinessId = @BusinessId
       AND p.Sku = d.Sku
    LEFT JOIN @AliasNormalization n
        ON n.Alias = d.Alias
)
MERGE dbo.ProductAliases AS target
USING AliasSource AS source
   ON target.BusinessId = @BusinessId
  AND target.ProductId = source.ProductId
  AND target.Scope = 0
  AND target.CustomerKey = N''
  AND target.NormalizedAlias = source.NormalizedAlias
WHEN MATCHED THEN
    UPDATE SET
        Alias = source.Alias,
        Kind = source.Kind,
        ResolutionMode = source.ResolutionMode,
        Source = 1,
        Status = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT
        (ProductAliasId, BusinessId, ProductId, Scope, CustomerKey, Alias,
         NormalizedAlias, Kind, ResolutionMode, Source, Status, UsageCount, CreatedAt)
    VALUES
        (NEWID(), @BusinessId, source.ProductId, 0, N'', source.Alias,
         source.NormalizedAlias, source.Kind, source.ResolutionMode, 1, 1, 0, GETUTCDATE())
WHEN NOT MATCHED BY SOURCE
     AND target.BusinessId = @BusinessId
     AND target.Scope = 0
     AND target.Source = 1 THEN
    DELETE;

-- Guard the filtered unique index and policy invariants before continuing.
IF EXISTS
(
    SELECT 1
    FROM dbo.ProductAliases
    WHERE BusinessId = @BusinessId
      AND Scope = 0
      AND Status = 1
      AND ResolutionMode = 1
    GROUP BY NormalizedAlias
    HAVING COUNT(DISTINCT ProductId) > 1
)
    THROW 51000, 'SeedMedidental: alias global AutoResolve ambiguo.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Products p
    WHERE p.BusinessId = @BusinessId
      AND p.IsActive = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ProductSearchTerms t
          WHERE t.BusinessId = p.BusinessId
            AND t.ProductId = p.ProductId
      )
)
    THROW 51000, 'SeedMedidental: producto activo sin ProductSearchTerms.', 1;

DECLARE @RecommendationRules TABLE
(
    ProductRecommendationRuleId UNIQUEIDENTIFIER NOT NULL,
    MatchType INT NOT NULL,
    SourceValue NVARCHAR(300) NOT NULL,
    RecommendedExternalProductId NVARCHAR(300) NOT NULL,
    RecommendedSku NVARCHAR(100) NULL,
    RecommendedSearchText NVARCHAR(300) NULL,
    RecommendationType INT NOT NULL,
    Priority INT NOT NULL,
    Reason NVARCHAR(500) NULL
);

MERGE dbo.ProductRecommendationRules AS target
USING @RecommendationRules AS source
   ON target.BusinessId = @BusinessId
  AND target.ProductRecommendationRuleId = source.ProductRecommendationRuleId
WHEN MATCHED THEN
    UPDATE SET
        IntegrationConnectionId = @LocalCommerceConnectionId,
        MatchType = source.MatchType,
        SourceProductId = NULL,
        SourceValue = source.SourceValue,
        RecommendedProductId = NULL,
        RecommendedExternalProductId = source.RecommendedExternalProductId,
        RecommendedSku = source.RecommendedSku,
        RecommendedSearchText = source.RecommendedSearchText,
        RecommendationType = source.RecommendationType,
        Priority = source.Priority,
        Reason = source.Reason,
        IsActive = 1,
        StartsAtUtc = NULL,
        EndsAtUtc = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT
        (ProductRecommendationRuleId, BusinessId, IntegrationConnectionId, MatchType,
         SourceProductId, SourceValue, RecommendedProductId, RecommendedExternalProductId,
         RecommendedSku, RecommendationType, Priority, Reason, IsActive, StartsAtUtc,
         EndsAtUtc, CreatedAt)
    VALUES
        (source.ProductRecommendationRuleId, @BusinessId, @LocalCommerceConnectionId,
         source.MatchType, NULL, source.SourceValue, NULL, source.RecommendedExternalProductId,
         source.RecommendedSku, source.RecommendationType, source.Priority, source.Reason,
         1, NULL, NULL, GETUTCDATE())
WHEN NOT MATCHED BY SOURCE
     AND target.BusinessId = @BusinessId
     AND target.IntegrationConnectionId = @LocalCommerceConnectionId THEN
    DELETE;



DECLARE @Hours TABLE (DayOfWeek INT NOT NULL, OpenTime TIME(0) NOT NULL, CloseTime TIME(0) NOT NULL);
INSERT INTO @Hours (DayOfWeek, OpenTime, CloseTime)
VALUES
(0, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '18:00')),
(1, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(2, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(3, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(4, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(5, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(6, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '18:00'));

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

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.4,
  "historyWindowSize": 24,
  "commerce": {
    "enabled": true,
    "provider": "Local",
    "conversation": {
      "contextualConfirmationPhrases": [
        "si",
        "si esa",
        "si es esa",
        "si ese",
        "si es ese",
        "si esta",
        "si es esta",
        "si este",
        "si es este",
        "si correcto",
        "si correcta",
        "confirmo",
        "correcto",
        "correcta",
        "esa",
        "ese",
        "esta",
        "este",
        "esa misma",
        "ese mismo",
        "la primera",
        "el primero"
      ],
      "candidateSelectionPhrases": [
        "esta",
        "esa",
        "primera",
        "primero",
        "segunda",
        "segundo",
        "tercera",
        "tercero",
        "ultima",
        "ultimo"
      ],
      "clauseSeparators": [
        "y",
        "e",
        "tambien",
        "ademas"
      ],
      "additionalRequestPhrases": [
        "otra",
        "otro",
        "adicional",
        "adicionales",
        "mas",
        "nuevamente",
        "tambien agrega",
        "tambien agregame",
        "tambien anade"
      ],
      "quantityWords": {
        "un": 1,
        "una": 1,
        "uno": 1,
        "dos": 2,
        "tres": 3,
        "cuatro": 4,
        "cinco": 5,
        "seis": 6,
        "siete": 7,
        "ocho": 8,
        "nueve": 9,
        "diez": 10,
        "once": 11,
        "doce": 12,
        "trece": 13,
        "catorce": 14,
        "quince": 15,
        "dieciseis": 16,
        "diecisiete": 17,
        "dieciocho": 18,
        "diecinueve": 19,
        "veinte": 20
      }
    },
    "pendingCart": {
      "discardOnFinalizeIssueCodes": [
        "product_unavailable"
      ]
    },
    "matching": {
      "exactNameDominanceMinimumMatches": 2,
      "candidateMentionSimilarity": 0.8,
      "pendingReferenceSimilarity": 0.78,
      "candidateSelectionSimilarity": 0.6
    }
  },
  "operatingHours": {
    "enforce": false,
    "outsideHours": {
      "guidance": "Responde de forma breve, cordial y cerrada. Explica que el negocio esta fuera de horario y que el proximo horario habil es {{next_operating_window}}. Adapta el mensaje a lo que dijo el cliente, pero no solicites datos, no prometas ejecutar gestiones, no abras catalogos y no termines con preguntas."
    }
  },
  "conversationFollowUp": {
    "enabled": true,
    "delayMinutes": 120,
    "guidance": "Retoma con calidez y brevedad la pregunta, eleccion o confirmacion concreta que sigue pendiente en el pedido. Usa el contexto vigente y formula una sola pregunta enfocada. No repitas catalogos, carritos ni resumenes completos; no agregues urgencia, descuentos, disponibilidad inventada ni promesas, y no modifiques el pedido.",
    "respectOperatingHours": true
  },
  "persona": "Eres el asistente comercial de Medidental por WhatsApp. Atiendes pedidos de equipos, materiales y consumibles odontologicos para profesionales y clinicas. Hablas en espanol de forma cercana, empatica, natural y servicial, como una persona atenta que acompana al cliente a armar su pedido. Usas parrafos cortos y espacios en blanco para que el mensaje sea facil de leer en WhatsApp. Evitas sonar como formulario, menu automatico o instruccion rigida. Puedes usar un emoji amable de manera ocasional, sin exagerar. Dirigete siempre al cliente como Doc, tenga o no tenga nombre registrado. Mantén un tono muy amable, cercano, respetuoso y profesional en todos los turnos; nunca uses el nombre personal del cliente para dirigirte a él. El catalogo y los resultados de las operaciones son la fuente de verdad comercial.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Llama siempre al cliente Doc, incluso si existe un nombre registrado; no uses el nombre personal como forma de trato.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## PRESENTACION\n\n- Presentate como asistente de Medidental con tono breve, amable y practico.\n- Dirigete siempre al cliente como Doc en la bienvenida, durante el pedido, al resolver dudas y en el cierre. Mantén siempre amabilidad, paciencia y disposición de ayuda.\n- Presenta catalogos, precios, carrito, totales y estado del pedido exclusivamente desde resultados oficiales del turno.",
  "messageSequences": {
    "order_created_customer": {
      "messages": [
        {
          "body": "Gracias por tu pedido, Doc. Lo recibimos correctamente y ya estamos coordinando la entrega."
        }
      ]
    },
    "order_created": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "order_created",
          "language": "es_CO",
          "bodyParameters": [
            "{order_number}",
            "{customer_name}",
            "{customer_phone}",
            "{city}",
            "{delivery_address}",
            "{items}",
            "{total}",
            "{currency}"
          ]
        }
      ]
    },
    "manual_payment_approval_request": {
      "messages": [
        {
          "type": "text",
          "body": "*Pago manual pendiente*\n\nPedido: {order_number}\nCliente: {customer_name}\nTelefono: {customer_phone}\nEntrega: {delivery_address}\nProductos: {items}\nTotal: ${amount} {currency}\n\nValida el pago antes de confirmarlo.",
          "buttons": [
            {
              "id": "manual_payment:confirm:{payment_transaction_id}",
              "title": "Confirmar pago"
            }
          ]
        }
      ]
    }
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 1000,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, caso mayorista especial o solicitud fuera del alcance.",
      "conversationGuidance": "Detecta ?nicamente una solicitud expl?cita de atenci?n humana, una queja que requiera intervenci?n o una negociaci?n especial fuera del alcance configurado.",
      "signal": {
        "type": "human_escalation",
        "description": "Solicitud expl?cita de hablar con una persona, queja que requiere intervenci?n o negociaci?n comercial especial fuera del alcance.",
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
    },
    {
      "id": "cart_mutation",
      "priority": 875,
      "goal": "Aplicar cambios explicitos al unico carrito activo desde cualquier etapa, sin depender del checkpoint conversacional.",
      "conversationGuidance": "Detecta order_changes solo ante una instruccion explicita de agregar, quitar o cambiar cantidades. La consulta de opciones pertenece a catalog_query. Esta capacidad es el fallback transversal; una stage que declare la misma senal tiene precedencia y es su unico propietario durante ese turno.",
      "signal": {
        "type": "order_changes",
        "description": "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
        "valueSchema": {
          "type": "array",
          "items": {
            "anyOf": [
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "add"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "number"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              },
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "set_quantity"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "number"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              },
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "remove",
                      "cancel_pending"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "null"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              }
            ]
          }
        },
        "ambiguityRules": [
          {
            "type": "distinct_values",
            "valueProperty": "destinationReference",
            "field": "delivery_address",
            "minimumDistinctValues": 2
          }
        ]
      },
      "actions": [
        {
          "id": "apply_order_changes",
          "operation": "commerce.apply_order_changes",
          "trigger": "on_signal",
          "signal": "order_changes",
          "arguments": {
            "commands": "{{signal.order_changes.value}}"
          },
          "onOutcome": {
            "cart.applied": {
              "response": {
                "guidance": "Confirma brevemente los cambios aplicados y continua segun el objetivo de la etapa."
              },
              "effects": [
                {
                  "type": "facts.clear",
                  "facts": [
                    "order_finalized",
                    "cart_review_confirmed",
                    "order_checkout_presented",
                    "customer_confirmed"
                  ]
                },
                {
                  "type": "presentation.add",
                  "template": "cart_snapshot",
                  "dataPath": "order",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.pending_cancelled": {
              "response": {
                "guidance": "Si discarded_items contiene productos, indica brevemente cuales productos agotados se dejaron fuera y continua inmediatamente con el cierre o el objetivo de la etapa, sin pedir otra confirmacion. Para otras cancelaciones, confirma brevemente la seleccion cancelada."
              }
            },
            "cart.product_not_found": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Indica que ese producto no se encontro y pide una descripcion o referencia mas precisa."
              }
            },
            "cart.product_ambiguous": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Presenta unicamente los candidatos devueltos y pregunta cual referencia desea."
              },
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "product_ambiguity",
                  "dataPath": "error.context",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.insufficient_stock": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
              },
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "insufficient_stock",
                  "dataPath": "error.context",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.item_not_found_or_ambiguous": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Aclara cual producto existente del pedido desea modificar."
              }
            },
            "cart.conflicting_commands": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplico ningun cambio del lote."
              }
            },
            "cart.multiple_destinations": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "No se aplico ningun cambio. Pregunta cual direccion debe usarse para entregar todo el unico pedido."
              }
            }
          },
          "execution": {
            "idempotency": "none"
          }
        }
      ]
    },
    {
      "id": "known_fact_lookup",
      "priority": 860,
      "goal": "Responder preguntas del cliente sobre datos conversacionales ya persistidos que la configuracion autoriza revelar.",
      "conversationGuidance": "Detecta known_fact_query cuando el cliente pregunta cual valor suyo o de su solicitud esta registrado, vigente o guardado. Resuelve referencias breves desde la pregunta inmediatamente anterior. Solo solicita claves incluidas en el enum y nunca uses esta senal para buscar productos, ejecutar cambios ni revelar facts tecnicos.",
      "signal": {
        "type": "known_fact_query",
        "description": "Consulta de solo lectura sobre uno o varios datos del cliente o de la solicitud que ya estan persistidos y autorizados para mostrarse.",
        "valueSchema": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fact_keys": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": [
                  "customer_name",
                  "company_name",
                  "delivery_method",
                  "city",
                  "delivery_address",
                  "delivery_reference",
                  "delivery_recipient_name",
                  "delivery_phone",
                  "payment_method"
                ]
              },
              "minItems": 1,
              "maxItems": 3
            }
          },
          "required": [
            "fact_keys"
          ]
        }
      },
      "actions": [
        {
          "id": "show_known_facts",
          "operation": "conversation.get_known_facts",
          "execution": {
            "idempotency": "none"
          },
          "trigger": "on_signal",
          "signal": "known_fact_query",
          "arguments": {
            "fact_keys": "{{signal.known_fact_query.value.fact_keys}}"
          },
          "onOutcome": {
            "known_facts.found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "known_facts",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "known_facts.not_found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "known_facts_missing",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            }
          }
        }
      ]
    },
    {
      "id": "catalog_lookup",
      "priority": 850,
      "goal": "Consultar el catalogo oficial cuando el cliente pregunte por productos, disponibilidad, referencias, precios u opciones, sin depender de la etapa activa.",
      "conversationGuidance": "Detecta catalog_query cuando el cliente solicita explorar el catalogo o consultar mercancia comprable. Para una pregunta abierta como que productos tienen, emite queries como una lista vacia. Cuando mencione productos, categorias o referencias concretas, incluye un termino util por cada busqueda. No uses palabras genericas como productos, catalogo, opciones o referencias como terminos. Si la pregunta pide recuperar o confirmar datos de entrega, direccion, recogida, pago, identidad, perfil, cliente u orden, emite cero catalog_query. Nunca respondas disponibilidad, nombres ni precios desde conocimiento general.",
      "signal": {
        "type": "catalog_query",
        "description": "Consulta de mercancia comprable del catalogo. Usa queries vacio para explorar una muestra amplia del catalogo y terminos concretos para buscar productos, categorias, referencias, precios o disponibilidad.",
        "valueSchema": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "queries": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "minItems": 0
            }
          },
          "required": [
            "queries"
          ]
        }
      },
      "actions": [
        {
          "id": "search_catalog_request",
          "operation": "commerce.search_products",
          "execution": {
            "idempotency": "none"
          },
          "trigger": "on_signal",
          "signal": "catalog_query",
          "arguments": {
            "queries": "{{signal.catalog_query.value.queries}}",
            "limit": 10
          },
          "onOutcome": {
            "products.not_found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_no_results",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "products.found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_results",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            }
          }
        }
      ]
    },
    {
      "id": "restart_order_request",
      "priority": 900,
      "goal": "Iniciar una solicitud de pedido nueva cuando el cliente indique inequivocamente que abandona la solicitud activa y quiere comenzar otra.",
      "conversationGuidance": "Detecta restart_request solo ante intencion explicita de comenzar un pedido nuevo o empezar de nuevo. Tambien aplica cuando ya se finalizo la seleccion del pedido activo y el cliente vuelve a saludar diciendo inequivocamente que quiere hacer un pedido. No lo detectes por un saludo solo, por ajustes al carrito vigente, por consultas de productos ni por expresiones de finalizacion como solo eso.",
      "signal": {
        "type": "restart_request",
        "description": "El cliente solicita abandonar la solicitud de pedido activa y comenzar un pedido nuevo desde cero.",
        "valueSchema": {
          "type": "boolean"
        }
      },
      "actions": [
        {
          "id": "reset_order_request",
          "operation": "conversation.reset_request",
          "trigger": "on_signal",
          "signal": "restart_request",
          "arguments": {},
          "execution": {
            "idempotency": "none"
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del odontologo o persona que realiza el pedido",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "customer",
      "extractionGuidance": "Extrae exclusivamente el nombre de la persona u odontologo que realiza el pedido. Nunca guardes aqui el nombre de un consultorio, clinica, empresa o establecimiento, ni el nombre de quien recibe la entrega."
    },
    {
      "key": "company_name",
      "role": "customer.company",
      "label": "nombre del consultorio, clinica o establecimiento",
      "type": "string",
      "required": false,
      "source": "user",
      "customerReadable": true,
      "scope": "customer",
      "extractionGuidance": "Extrae exclusivamente el nombre del consultorio, clinica, empresa o establecimiento. Nunca lo conviertas en customer_name ni asumas que identifica a la persona que realiza el pedido."
    },
    {
      "key": "order_finalized",
      "role": "order.finalized",
      "label": "cliente finalizo el carrito",
      "type": "boolean",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Representa que el cliente comunico que termino la seleccion de productos y desea continuar con el pedido."
    },
    {
      "key": "cart_review_confirmed",
      "role": "order.cart_review_confirmed",
      "label": "carrito aprobado por el cliente",
      "type": "boolean",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Representa la aprobacion explicita del resumen vigente del carrito."
    },
    {
      "key": "delivery_method",
      "role": "shipping.method",
      "label": "modalidad de entrega",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Normaliza la modalidad elegida al valor canonico configurado para entrega o recogida.",
      "options": [
        {
          "value": "domicilio",
          "label": "Domicilio"
        },
        {
          "value": "recogida",
          "label": "Recogida"
        }
      ]
    },
    {
      "key": "city",
      "role": "shipping.city",
      "label": "ciudad de entrega",
      "type": "string",
      "required": true,
      "source": "system",
      "customerReadable": true,
      "defaultValue": "Valledupar",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "delivery_address",
      "role": "shipping.address",
      "label": "direccion de entrega o recogida",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae solo la ubicacion fisica. Si el mismo mensaje incluye telefono o celular, excluye de la direccion el numero telefonico y expresiones de enlace como y el telefono es, y el numero es o variantes con errores ortograficos."
    },
    {
      "key": "delivery_reference",
      "role": "shipping.reference",
      "label": "barrio, apartamento o referencia complementaria de entrega",
      "type": "string",
      "required": false,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae solo detalles complementarios para localizar la entrega, como barrio, urbanizacion, apartamento, interior, bloque, indicaciones o un punto de referencia. No copies el telefono ni el nombre del receptor."
    },
    {
      "key": "delivery_recipient_name",
      "role": "shipping.recipient_name",
      "label": "nombre de quien recibe el pedido",
      "type": "string",
      "required": false,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae este dato solo cuando el mensaje identifica a una persona como quien recibe, receptor o contacto de entrega. Nunca lo conviertas en customer_name ni asumas que cambia la identidad del cliente."
    },
    {
      "key": "delivery_phone",
      "role": "customer.phone",
      "label": "celular de entrega",
      "type": "phone",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "customer"
    },
    {
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Normaliza la eleccion al metodo de pago canonico configurado.",
      "options": [
        {
          "value": "efectivo",
          "label": "Efectivo"
        },
        {
          "value": "transferencia",
          "label": "Transferencia"
        },
        {
          "value": "datafono",
          "label": "Datáfono"
        }
      ]
    },
    {
      "key": "order_checkout_presented",
      "role": "order.checkout_presented",
      "label": "resumen final presentado",
      "type": "boolean",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "system.recipe_catalog_queries",
      "role": "system.recipe_catalog_queries",
      "label": "consultas de catalogo derivadas de receta",
      "type": "json",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "customer_confirmed",
      "role": "confirmation.verbal",
      "label": "confirmacion verbal del pedido",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "dependsOn": [
        "order_checkout_presented",
        "cart_review_confirmed",
        "delivery_method",
        "city",
        "delivery_address",
        "delivery_reference",
        "delivery_recipient_name",
        "delivery_phone",
        "customer_name",
        "company_name",
        "payment_method"
      ],
      "retentionDays": 1,
      "extractionGuidance": "Representa la confirmacion explicita del resumen final vigente."
    }
  ],
  "notifications": {
    "order_created": {
      "enabled": true,
      "deliveries": [
        {
          "id": "customer",
          "enabled": true,
          "recipients": [
            "source:conversation"
          ],
          "sendMessageSequence": "order_created_customer"
        },
        {
          "id": "internal",
          "enabled": true,
          "recipients": [
            "inbound:payment_approver"
          ],
          "sendMessageSequence": "order_created"
        }
      ]
    },
    "manual_payment_requested": {
      "enabled": true,
      "deliveries": [
        {
          "id": "internal",
          "enabled": true,
          "recipients": [
            "inbound:payment_approver"
          ],
          "sendMessageSequence": "manual_payment_approval_request"
        }
      ]
    }
  },
  "webhooks": {
    "wompi": {}
  },
  "escalations": {
    "human": {
      "contacts": []
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "order": {
        "requiredFactRoles": {},
        "paymentMethods": {
          "efectivo": {
            "label": "efectivo al recibir",
            "aliases": [
              "efectivo",
              "contraentrega"
            ],
            "template": "order_checkout_no_payment"
          },
          "datafono": {
            "label": "datafono al recibir",
            "aliases": [
              "datafono",
              "datáfono",
              "tarjeta",
              "pago con tarjeta"
            ],
            "template": "order_checkout_card_terminal"
          },
          "transferencia": {
            "label": "transferencia manual",
            "aliases": [
              "transferencia",
              "nequi",
              "bancolombia"
            ],
            "template": "order_checkout_manual_transfer",
            "manualConfirmationRequired": true,
            "manualExpirationMinutes": 1440,
            "confirmationOutcome": "order_paid"
          }
        },
        "shipping": {
          "enabled": true,
          "localCity": "Valledupar",
          "localCost": 6000,
          "nationalCost": 25000
        }
      }
    }
  },
  "conversationOpening": {
    "enabled": true,
    "guidance": "Escribe exactamente esta bienvenida como primer parrafo: ¡Hola, Doc! Bienvenido a Medidental. Es un gusto atenderle 😊 Usa Doc aunque conozcas o no el nombre del cliente. No uses el nombre personal del cliente. No agregues otra frase, despedida ni pregunta en este primer parrafo. No menciones el tipo de cliente, ciudad, direccion, telefono, compras anteriores ni otros datos recordados. La continuacion, separada por una linea en blanco, debe seguir el objetivo de la etapa.",
    "allowQuestions": false
  },
  "failureResponses": {
    "llmUnavailable": "Lo siento, en este momento tengo un inconveniente temporal para procesar tu mensaje. Por favor, intenta nuevamente en unos minutos."
  },
  "templates": {
    "order_checkout_no_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: Doc\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?",
    "order_checkout_card_terminal": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: Doc\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: datafono al recibir\n\nLlevaremos el datafono para realizar el pago al momento de la entrega. Confirmas tu pedido con esta informacion?",
    "order_checkout_manual_transfer": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: Doc\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nTu pago queda pendiente de confirmacion manual. Un agente del equipo de Medidental confirmara el pago; cuando se confirme, te notificaremos que el pedido fue creado.",
    "catalog_results": "{{#if search_text}}Claro, encontre estas opciones para ti:\r\n\r\n*Productos disponibles*\r\n\r\n{{#each products}}\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{/each}}\r\n\r\n{{#each recommendations}}\r\n\r\n*Tambien te puede servir*\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{#if reason}}{{reason}}\r\n{{/if}}{{/each}}\r\n\r\nCual te interesa y cuantas unidades deseas agregar?{{else}}Tenemos equipos, materiales y consumibles odontologicos para profesionales y clinicas. Estos son algunos de nuestros productos:\r\n\r\n{{#each products}}\r\n- {{name}}\r\n{{/each}}\r\n\r\nCual de estos productos le interesa? Tambien puedo ayudarle a encontrar otro producto que necesite.{{/if}}",
    "catalog_no_results": "Por ahora no encontre {{#if search_text}}{{search_text}} disponibles{{else}}productos disponibles para esa busqueda{{/if}} en nuestro catalogo.\r\n\r\nSi quieres, puedo buscar una opcion parecida o ayudarte a elegir otro producto.",
    "known_facts": "Claro. Esto es lo que tengo registrado:\r\n\r\n{{#each facts}}\r\n- {{label}}: {{value}}\r\n{{/each}}",
    "known_facts_missing": "No tengo ese dato registrado todavia. Si quieres, puedes indicarmelo o actualizarlo.",
    "recipe_results": "Buena idea. Puedes inspirarte con estas preparaciones:\r\n\r\n*Ideas para preparar*\r\n{{#each results}}\r\n- {{Title}}\r\n  {{Url}}\r\n{{/each}}",
    "cart_snapshot": "Listo, ya actualice tu pedido 🙌\r\n\r\n*Pedido actual*\r\n\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\nQuieres agregar algo mas? Cuando hayas terminado de elegir, avisame y continuamos.",
    "cart_review": "Perfecto, revisemos juntos que todo este bien:\r\n\r\n*Resumen de tu pedido*\r\n\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\nLo ves correcto o quieres cambiar algo?",
    "product_ambiguity": "Quiero asegurarme de agregar la opcion correcta. Para {{product_text}} encontre:\r\n{{#each product_options}}\r\n- {{Name}}: ${{UnitPrice}} {{Currency}}\r\n{{/each}}\r\n\r\nCual prefieres? Conservare los demas productos de tu solicitud.",
    "insufficient_stock": "Puedo ayudarte con esa referencia, pero la cantidad solicitada supera el inventario disponible.\r\n\r\n- Producto: {{product_text}}\r\n- Solicitado en total: {{requested_quantity}}\r\n- Disponible: {{available_quantity}}\r\n\r\nPara este cambio, indica una cantidad de hasta {{maximum_command_quantity}}; los demas cambios del lote aun no se han aplicado."
  },
  "flows": [
    {
      "id": "order",
      "type": "primary",
      "routingGuidance": "Use this primary flow for Medidental product orders, customer identification, catalog-grounded recommendations, delivery data, payment method and order confirmation.",
      "stages": [
        {
          "id": "product_selection",
          "name": "Productos, catalogo y recomendaciones",
          "goal": "Recibir pedidos abiertos, resolver productos reales del catalogo, recomendar de forma controlada y construir el carrito hasta que el cliente finalice.",
          "response": {},
          "advanceWhenFacts": [
            "order_finalized"
          ],
          "conversationGuidance": "Acompana al cliente de forma cercana mientras elige productos. Al abrir una solicitud sin una consulta o seleccion concreta, explica simplemente que estas para ayudarle con su pedido y pregunta que desea el dia de hoy, sin repetir la bienvenida ni mencionar su perfil, ubicacion o categorias supuestas. Las consultas comerciales se presentan con resultados autoritativos del catalogo. Elegir una referencia ofrecida por una consulta no la agrega al pedido: si aun no hay cantidad, pregunta cuantas unidades desea y nunca supongas una unidad. Cuando el cliente indique la cantidad, conserva la referencia elegida desde la conversacion inmediata y aplica un unico cambio. Las solicitudes de preparacion producen ideas de receta y productos relacionados en el mismo turno. Cuando solicite productos y cantidades, conserva el lote completo para que el motor lo aplique al unico pedido activo. Tras cada cambio presenta el estado vigente con una transicion natural. Cuando el cliente comunique que termino la seleccion, registra order_finalized=true.",
          "collect": [
            "order_finalized",
            "delivery_method",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "payment_method"
          ],
          "signals": [
            {
              "type": "recipe_request",
              "description": "Solicitud de ideas para preparar una comida. El valor contiene el ingrediente o la preparacion principal que debe buscarse.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_changes",
              "description": "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "anyOf": [
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "add"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "set_quantity"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "remove",
                            "cancel_pending"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "null"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    }
                  ]
                }
              },
              "ambiguityRules": [
                {
                  "type": "distinct_values",
                  "valueProperty": "destinationReference",
                  "field": "delivery_address",
                  "minimumDistinctValues": 2
                }
              ]
            }
          ],
          "actions": [
            {
              "id": "search_recipe_request",
              "operation": "commerce.search_recipes",
              "execution": {
                "idempotency": "none"
              },
              "trigger": "on_signal",
              "signal": "recipe_request",
              "arguments": {
                "ingredient": "{{signal.recipe_request.value}}",
                "query": "preparacion facil",
                "limit": 2
              },
              "onOutcome": {
                "recipes.found": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "system.recipe_catalog_queries": "catalog_search_queries"
                      }
                    },
                    {
                      "type": "presentation.add",
                      "template": "recipe_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ],
                  "response": {
                    "guidance": "Presenta mÃ¡ximo dos ideas devueltas y luego muestra Ãºnicamente ingredientes encontrados en el catÃ¡logo oficial."
                  }
                }
              }
            },
            {
              "id": "search_recipe_catalog_products",
              "operation": "commerce.search_products",
              "execution": {
                "idempotency": "none"
              },
              "trigger": "when_ready",
              "condition": {
                "factPresent": "system.recipe_catalog_queries"
              },
              "arguments": {
                "queries": "{{fact.system.recipe_catalog_queries}}",
                "limit": 10
              },
              "onOutcome": {
                "products.not_found": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "system.recipe_catalog_queries"
                      ]
                    },
                    {
                      "type": "presentation.add",
                      "template": "catalog_no_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "products.found": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "system.recipe_catalog_queries"
                      ]
                    },
                    {
                      "type": "presentation.add",
                      "template": "catalog_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ],
                  "response": {
                    "guidance": "Muestra solo productos reales devueltos por catÃ¡logo, con presentaciÃ³n y precio cuando estÃ©n disponibles."
                  }
                }
              }
            },
            {
              "id": "apply_order_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "order_changes",
              "arguments": {
                "commands": "{{signal.order_changes.value}}"
              },
              "onOutcome": {
                "cart.applied": {
                  "response": {
                    "guidance": "Confirma brevemente los cambios aplicados y continÃºa segÃºn el objetivo de la etapa."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "cart_snapshot",
                      "dataPath": "order",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.pending_cancelled": {
                  "response": {
                    "guidance": "Si discarded_items contiene productos, indica brevemente cuales productos agotados se dejaron fuera y continua inmediatamente con el cierre o el objetivo de la etapa, sin pedir otra confirmacion. Para otras cancelaciones, confirma brevemente la seleccion cancelada."
                  }
                },
                "cart.product_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que ese producto no se encontrÃ³ y pide una descripciÃ³n o referencia mÃ¡s precisa."
                  }
                },
                "cart.product_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l referencia desea."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "product_ambiguity",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.insufficient_stock": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "insufficient_stock",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l producto existente del pedido desea modificar."
                  }
                },
                "cart.conflicting_commands": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplicÃ³ ningÃºn cambio del lote."
                  }
                },
                "cart.multiple_destinations": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "No se aplicÃ³ ningÃºn cambio. Pregunta cuÃ¡l direcciÃ³n debe usarse para entregar todo el Ãºnico pedido."
                  }
                }
              }
            }
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "customer_identity",
          "name": "Identificacion del cliente",
          "goal": "Obtener el nombre de la persona u odontologo que realiza el pedido y conservar por separado el establecimiento cuando el cliente lo informe.",
          "response": {},
          "advanceWhenFacts": [
            "customer_name"
          ],
          "conversationGuidance": "Si falta customer_name, solicita exclusivamente el nombre de la persona u odontologo que realiza el pedido. Si el cliente menciona un consultorio, clinica, empresa o establecimiento, registra ese dato como company_name y no como customer_name; si aun falta el nombre personal, pidelo de forma breve. company_name es opcional y nunca bloquea el avance. No repitas el saludo ni la bienvenida.",
          "collect": [
            "customer_name",
            "company_name"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "cart_review",
          "name": "Transicion al cierre",
          "goal": "Continuar hacia entrega y pago sin mostrar un resumen intermedio.",
          "response": {},
          "advanceWhenFacts": [
            "order_finalized"
          ],
          "conversationGuidance": "Cuando el cliente termine de agregar productos, avanza directamente a modalidad y datos de entrega. No muestres ni solicites confirmacion de un resumen intermedio; el unico resumen de cierre se presenta despues de completar entrega y pago.",
          "collect": [
            "order_finalized",
            "delivery_method",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "payment_method"
          ],
          "signals": [
            {
              "type": "order_changes",
              "description": "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "anyOf": [
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "add"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "set_quantity"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "remove",
                            "cancel_pending"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "null"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    }
                  ]
                }
              },
              "ambiguityRules": [
                {
                  "type": "distinct_values",
                  "valueProperty": "destinationReference",
                  "field": "delivery_address",
                  "minimumDistinctValues": 2
                }
              ]
            }
          ],
          "actions": [
            {
              "id": "show_current_order_draft",
              "operation": "commerce.get_order_draft",
              "trigger": "when_ready",
              "condition": {
                "factMissing": "order_finalized"
              },
              "arguments": {},
              "onOutcome": {
                "order.draft_loaded": {
                  "response": {
                    "guidance": "Muestra los Ã­tems, cantidades, subtotales y total devueltos, y pregunta si el pedido actual estÃ¡ correcto."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "cart_review",
                      "dataPath": "order",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "order.draft_empty": {
                  "response": {
                    "guidance": "Informa que el pedido vigente esta vacio y ayuda al cliente a elegir productos antes de continuar."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "cart_review_confirmed"
                      ]
                    }
                  ]
                }
              }
            },
            {
              "id": "apply_order_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "order_changes",
              "arguments": {
                "commands": "{{signal.order_changes.value}}"
              },
              "onOutcome": {
                "cart.applied": {
                  "response": {
                    "guidance": "Confirma brevemente los cambios aplicados y continÃºa segÃºn el objetivo de la etapa."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "cart_review",
                      "dataPath": "order",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.pending_cancelled": {
                  "response": {
                    "guidance": "Si discarded_items contiene productos, indica brevemente cuales productos agotados se dejaron fuera y continua inmediatamente con el cierre o el objetivo de la etapa, sin pedir otra confirmacion. Para otras cancelaciones, confirma brevemente la seleccion cancelada."
                  }
                },
                "cart.product_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que ese producto no se encontrÃ³ y pide una descripciÃ³n o referencia mÃ¡s precisa."
                  }
                },
                "cart.product_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l referencia desea."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "product_ambiguity",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.insufficient_stock": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "insufficient_stock",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l producto existente del pedido desea modificar."
                  }
                },
                "cart.conflicting_commands": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplicÃ³ ningÃºn cambio del lote."
                  }
                },
                "cart.multiple_destinations": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "No se aplicÃ³ ningÃºn cambio. Pregunta cuÃ¡l direcciÃ³n debe usarse para entregar todo el Ãºnico pedido."
                  }
                }
              }
            }
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "order_data",
          "name": "Entrega",
          "goal": "Definir recogida o domicilio y obtener solo los datos faltantes requeridos por el checkout.",
          "response": {},
          "advanceWhenFacts": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name"
          ],
          "reentryOnFactChanged": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "customer_name"
          ],
          "conversationGuidance": "Despues de que el cliente termine de agregar productos pregunta unicamente si prefiere recogida o domicilio, sin mostrar un resumen intermedio. Deja esa eleccion en un mensaje separado. Para recogida, registra la modalidad y el punto configurado como direccion. Cuando elija domicilio, solicita todos los datos faltantes en un solo mensaje breve y estructurado con esta lista: direccion completa; barrio, apartamento o referencia complementaria; nombre de quien recibe; y celular de entrega. Pide solo los que falten, permite responder todo junto y no envies una pregunta separada por cada dato. La referencia complementaria sigue siendo opcional: puede responder no aplica y nunca debe detener el flujo. delivery_recipient_name identifica a quien recibe y nunca reemplaza customer_name. Usa la ciudad por defecto configurada salvo que el cliente indique otra.",
          "collect": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "payment_method"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "payment_method",
          "name": "Metodo de pago",
          "goal": "Elegir uno de los metodos de pago configurados para Medidental.",
          "response": {},
          "advanceWhenFacts": [
            "payment_method"
          ],
          "conversationGuidance": "Cuando la modalidad de entrega y los datos requeridos esten completos, pregunta como desea realizar el pago y presenta en una lista breve las tres opciones configuradas: efectivo, transferencia o datafono. Registra exactamente payment_method=efectivo, payment_method=transferencia o payment_method=datafono segun responda. No menciones metodos no configurados.",
          "collect": [
            "payment_method"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "summary",
          "name": "Resumen final del pedido",
          "goal": "Preparar y mostrar el resumen oficial con entrega, pago y total final del motor.",
          "response": {},
          "advanceWhenFacts": [
            "order_checkout_presented"
          ],
          "reentryOnFactChanged": [
            "order_finalized",
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "customer_name",
            "payment_method"
          ],
          "actions": [
            {
              "id": "prepare_order_checkout",
              "operation": "commerce.prepare_checkout",
              "trigger": "when_ready",
              "condition": {
                "all": [
                  {
                    "factPresent": "order_finalized"
                  },
                  {
                    "factPresent": "delivery_method"
                  },
                  {
                    "factPresent": "city"
                  },
                  {
                    "factPresent": "delivery_address"
                  },
                  {
                    "factPresent": "delivery_phone"
                  },
                  {
                    "factPresent": "customer_name"
                  },
                  {
                    "factPresent": "payment_method"
                  },
                  {
                    "factMissing": "order_checkout_presented"
                  }
                ]
              },
              "arguments": {},
              "onOutcome": {
                "order.checkout_ready": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order.checkout_payment_required": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order.checkout_pending_manual_payment": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order_draft_missing": {
                  "response": {
                    "guidance": "Informa que no fue posible recuperar el pedido vigente y pide intentar nuevamente para continuar."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "cart_review_confirmed",
                        "order_checkout_presented"
                      ]
                    }
                  ]
                },
                "missing_prerequisites": {
                  "response": {
                    "guidance": "Informa que faltan datos vigentes para preparar el resumen y solicita unicamente el siguiente dato requerido por la etapa."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "cart_review_confirmed",
                        "order_checkout_presented"
                      ]
                    }
                  ]
                }
              }
            }
          ],
          "conversationGuidance": "Cuando ya existan items, carrito aprobado, entrega y metodo de pago, el motor prepara el checkout una sola vez. Si el metodo es efectivo, muestra el resumen autoritativo renderizado por el motor y pide confirmacion verbal. Si el metodo es transferencia, muestra el resumen autoritativo e informa que el pago queda pendiente de confirmacion manual por el equipo; no pidas comprobante ni confirmacion adicional. Si el metodo es datafono, muestra el resumen autoritativo, informa que se llevara el datafono para pagar al recibir y pide confirmacion verbal exactamente igual que con efectivo. Si falla por configuracion no recuperable, escala a humano.",
          "collect": [
            "order_checkout_presented"
          ],
          "awaitCustomerReply": true,
          "transitions": [
            {
              "id": "summary_to_manual_payment_pending",
              "priority": 20,
              "condition": {
                "all": [
                  {
                    "factPresent": "order_checkout_presented"
                  },
                  {
                    "factEquals": {
                      "key": "payment_method",
                      "value": "transferencia"
                    }
                  }
                ]
              },
              "to": "manual_payment_pending"
            },
            {
              "id": "summary_to_order_confirmation",
              "priority": 10,
              "condition": {
                "factPresent": "order_checkout_presented"
              },
              "to": "order_confirmation"
            }
          ]
        },
        {
          "id": "order_confirmation",
          "name": "Confirmacion del pedido",
          "goal": "Crear el pedido despues de confirmacion del cliente.",
          "response": {},
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "actions": [
            {
              "id": "create_confirmed_delivery_payment_order",
              "operation": "commerce.create_order",
              "trigger": "when_ready",
              "condition": {
                "all": [
                  {
                    "any": [
                      {
                        "factEquals": {
                          "key": "payment_method",
                          "value": "efectivo"
                        }
                      },
                      {
                        "factEquals": {
                          "key": "payment_method",
                          "value": "datafono"
                        }
                      }
                    ]
                  },
                  {
                    "factEquals": {
                      "key": "customer_confirmed",
                      "value": true
                    }
                  }
                ]
              },
              "arguments": {
                "customer_confirmed": "{{fact.customer_confirmed}}"
              },
              "onOutcome": {
                "order.created": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ],
                  "response": {
                    "suppressText": true
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Si payment_method=transferencia, no pidas confirmacion verbal, no confirmes que el pedido fue creado y responde que el pago queda pendiente de confirmacion manual por el equipo de Medidental; cuando el pago se confirme manualmente, el sistema notificara que el pedido fue creado. Si payment_method=efectivo o payment_method=datafono y falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Con customer_confirmed=true y metodo efectivo o datafono, crea el pedido usando los facts vigentes. Para datafono, recuerda que se llevara el dispositivo y no afirmes que el pago ya fue recibido. Despues de crear el pedido envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
          "collect": [
            "customer_confirmed"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "manual_payment_pending",
          "name": "Pago pendiente de aprobacion",
          "goal": "Mantener la solicitud a la espera de la confirmacion manual del equipo sin pedir otra respuesta al cliente.",
          "conversationGuidance": "Informa brevemente que la transferencia sigue pendiente de validacion por el equipo y que se notificara el resultado. No solicites una confirmacion adicional.",
          "collect": [],
          "actions": [],
          "transitions": [],
          "response": {}
        }
      ]
    }
  ]
}';
DECLARE @PartialCartOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Da un resultado explicito para cada referencia del lote usando la presentacion deterministica: agregada, sin existencia, ambigua, sugerida, cantidad insuficiente o no encontrada. No omitas referencias ni las mezcles entre categorias."},"effects":[{"type":"presentation.add","template":"cart_partial","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductSuggestionOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Presenta la sugerencia devuelta y pide confirmacion explicita antes de agregarla."},"effects":[{"type":"presentation.add","template":"product_ambiguity","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductUnavailableOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica que la referencia identificada no esta disponible y solicita otra opcion; no afirmes que fue agregada."},"effects":[{"type":"presentation.add","template":"cart_product_unavailable","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductNotFoundOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica las referencias que no tuvieron coincidencia segura y solicita datos mas precisos; no afirmes que el carrito cambio."},"effects":[{"type":"presentation.add","template":"cart_not_found","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    N'Procesé cada producto de tu solicitud:\r\n{{#if applied_items}}\r\n*Agregados*\r\n{{#each applied_items}}\r\n- {{name}} — cantidad: {{quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if unavailable_items}}\r\n*Sin existencia*\r\n{{#each unavailable_items}}\r\n- {{product_text}}{{#if recognized_name}} ({{recognized_name}}){{/if}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if insufficient_stock_items}}\r\n*Existencia insuficiente*\r\n{{#each insufficient_stock_items}}\r\n- {{product_text}}: solicitaste {{requested_quantity}} y hay {{available_quantity}}; puedes pedir hasta {{maximum_command_quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if ambiguous_options}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_options}}\r\n- Para {{product_text}}: {{name}} — ${{unit_price}} {{currency}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if suggested_options}}\r\n*Necesito confirmar*\r\n{{#each suggested_options}}\r\n- Para {{product_text}}: ¿te refieres a {{name}} — ${{unit_price}} {{currency}}?\r\n{{/each}}\r\n{{/if}}\r\n{{#if not_found_items}}\r\n*No encontrados*\r\n{{#each not_found_items}}\r\n- {{product_text}}\r\n{{/each}}\r\n{{/if}}\r\n*Pedido actual*\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\nIndícame las elecciones o una referencia más precisa para los pendientes.');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'— ${{unit_price}} {{currency}}', N'— {{availability_text}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'{{product_text}}{{#if recognized_name}} ({{recognized_name}}){{/if}}', N'{{description}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'{{#if ambiguous_options}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_options}}\r\n- Para {{product_text}}: {{name}} — {{availability_text}}\r\n{{/each}}\r\n{{/if}}',
        N'{{#if ambiguous_groups}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_groups}}\r\nPara {{product_text}}, necesito que me confirmes una de estas opciones:\r\n{{options_text}}\r\n{{/each}}\r\n{{/if}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'*Pedido actual*\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\n',
        N'*Total actual del pedido: ${{total}} {{currency}}*\r\n\r\n'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_not_found',
    N'No agregue estas referencias porque no encontre una coincidencia segura:\r\n{{#each issues}}\r\n- {{ProductText}}\r\n{{/each}}\r\n\r\nIndicame el nombre, marca, presentacion o codigo de una de ellas para identificarla.');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_product_unavailable',
    N'Reconozco la referencia "{{product_text}}", pero actualmente no tiene disponibilidad comercial para agregarla.\r\n\r\nNo hice cambios al pedido por esta referencia. Puedes indicarme otra marca, presentacion o producto.');

IF JSON_VALUE(@SettingsJson, '$.globalActions[1].actions[0].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedMedidental: ruta global de carrito inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.flows[0].stages[0].actions[2].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedMedidental: ruta product_selection de carrito inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.flows[0].stages[2].actions[1].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedMedidental: ruta cart_review de carrito inesperada.', 1;

DECLARE @CartExecutionPaths TABLE (Path NVARCHAR(400) NOT NULL);
INSERT INTO @CartExecutionPaths (Path) VALUES
    (N'$.globalActions[1].actions[0].execution'),
    (N'$.flows[0].stages[0].actions[2].execution'),
    (N'$.flows[0].stages[2].actions[1].execution');

DECLARE @CartExecutionPath NVARCHAR(400);
DECLARE CartExecutionCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Path FROM @CartExecutionPaths;
OPEN CartExecutionCursor;
FETCH NEXT FROM CartExecutionCursor INTO @CartExecutionPath;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartExecutionPath,
        JSON_QUERY(N'{"idempotency":"input_version","timeoutSeconds":240,"maxAttempts":1}'));
    FETCH NEXT FROM CartExecutionCursor INTO @CartExecutionPath;
END;
CLOSE CartExecutionCursor;
DEALLOCATE CartExecutionCursor;

DECLARE @CartOutcomePaths TABLE (Path NVARCHAR(400) NOT NULL);
INSERT INTO @CartOutcomePaths (Path) VALUES
    (N'$.globalActions[1].actions[0].onOutcome'),
    (N'$.flows[0].stages[0].actions[2].onOutcome'),
    (N'$.flows[0].stages[2].actions[1].onOutcome');

DECLARE @CartOutcomePath NVARCHAR(400);
DECLARE CartOutcomeCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Path FROM @CartOutcomePaths;
OPEN CartOutcomeCursor;
FETCH NEXT FROM CartOutcomeCursor INTO @CartOutcomePath;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.partially_applied"', JSON_QUERY(@PartialCartOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_suggestion"', JSON_QUERY(@ProductSuggestionOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_unavailable"', JSON_QUERY(@ProductUnavailableOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_not_found"', JSON_QUERY(@ProductNotFoundOutcome));
    FETCH NEXT FROM CartOutcomeCursor INTO @CartOutcomePath;
END
CLOSE CartOutcomeCursor;
DEALLOCATE CartOutcomeCursor;

-- Retoma solo respuestas que dejan una pregunta, eleccion o confirmacion pendiente.
IF JSON_VALUE(@SettingsJson, '$.globalActions[2].id') <> 'known_fact_lookup'
    THROW 51000, 'SeedMedidental: ruta global de facts conocidos inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.globalActions[3].id') <> 'catalog_lookup'
    THROW 51000, 'SeedMedidental: ruta global de catalogo inesperada.', 1;



-- La transferencia manual queda esperando al equipo, no al cliente.
SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.flows[0].stages[5].actions[0].onOutcome."order.checkout_pending_manual_payment".response', JSON_QUERY(N'{}'));

DECLARE @FollowUpCartOutcomePath NVARCHAR(400);
DECLARE FollowUpCartOutcomeCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Path FROM @CartOutcomePaths;
OPEN FollowUpCartOutcomeCursor;
FETCH NEXT FROM FollowUpCartOutcomeCursor INTO @FollowUpCartOutcomePath;
WHILE @@FETCH_STATUS = 0
BEGIN
    FETCH NEXT FROM FollowUpCartOutcomeCursor INTO @FollowUpCartOutcomePath;
END
CLOSE FollowUpCartOutcomeCursor;
DEALLOCATE FollowUpCartOutcomeCursor;
IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedMedidental: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, [Name], [Description], IsActive,
         SettingsJson, Model, Temperature, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente Medidental',
         N'Asistente comercial para pedidos, catalogo dental y recomendaciones de productos.',
         1, @SettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        [Name] = N'Asistente Medidental',
        [Description] = N'Asistente comercial para pedidos, catalogo dental y recomendaciones de productos.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        Model = N'gpt-4.1-mini',
        Temperature = 0.4,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

DECLARE @SubscriptionPlanId UNIQUEIDENTIFIER;

SELECT @SubscriptionPlanId = SubscriptionPlanId
FROM dbo.SubscriptionPlans
WHERE Code = N'essential'
  AND IsActive = 1;

IF @SubscriptionPlanId IS NULL
BEGIN
    THROW 51000, 'SeedMedidental: plan essential activo no encontrado; no se puede completar el aprovisionamiento.', 1;
END

IF NOT EXISTS (
    SELECT 1
    FROM dbo.BusinessSubscriptions
    WHERE BusinessId = @BusinessId
)
BEGIN
    INSERT INTO dbo.BusinessSubscriptions (
        BusinessId,
        SubscriptionPlanId,
        [Status],
        CurrentPeriodStart,
        CurrentPeriodEnd,
        PlanCodeSnapshot,
        PlanNameSnapshot,
        MonthlyPriceCop,
        IncludedCredits,
        MaxVariableCostCop,
        MaxVariableCostPercent,
        ExtraCredits,
        ExtraVariableCostCop,
        AutoRenew
    )
    SELECT
        @BusinessId,
        SubscriptionPlanId,
        1,
        DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
        DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),
        Code,
        [Name],
        MonthlyPriceCop,
        IncludedCredits,
        MaxVariableCostCop,
        MaxVariableCostPercent,
        0,
        0,
        1
    FROM dbo.SubscriptionPlans
    WHERE SubscriptionPlanId = @SubscriptionPlanId;
END

PRINT N'SeedMedidental: negocio, comercio local, agente y suscripcion configurados.';

GO
