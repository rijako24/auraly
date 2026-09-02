-- =============================================================================
-- MigrateDigitalShopWhatsAppToCJ.sql
--
-- Deja como ruta final el numero Meta asignado a CJ Distribuciones.
-- Conserva sus credenciales si el numero ya existe. Si el token aun no fue
-- cargado, crea una configuracion preparada e inactiva para no simular que el
-- canal esta listo antes de poder autenticarse contra Meta.
-- =============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF LOWER(N'$(DeploymentEnvironment)') = N'prod'
BEGIN
    PRINT N'MigrateDigitalShopWhatsAppToCJ: migración de demostración omitida en producción.';
    RETURN;
END;

DECLARE @CJBusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @CJAgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000020';
DECLARE @CJCommerceConnectionId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000030';
DECLARE @CJWhatsAppNumberId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000050';
DECLARE @PhoneNumber NVARCHAR(20) = N'573117323198';
DECLARE @WhatsAppPhoneNumberId NVARCHAR(100) = N'1234810033044432';
DECLARE @WhatsAppBusinessAccountId NVARCHAR(100) = N'4841200399440958';
DECLARE @ConfiguredAccessToken NVARCHAR(500) = NULLIF(LTRIM(RTRIM(N'$(CJWhatsAppAccessToken)')), N'');
DECLARE @WhatsAppNumberId UNIQUEIDENTIFIER;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Agents
    WHERE AgentId = @CJAgentId
      AND BusinessId = @CJBusinessId
      AND IsActive = 1
)
BEGIN
    THROW 51000, 'MigrateDigitalShopWhatsAppToCJ: agente CJ activo no encontrado.', 1;
END

SELECT TOP (1)
    @WhatsAppNumberId = BusinessWhatsAppNumberId
FROM dbo.BusinessWhatsAppNumbers
WHERE WhatsAppPhoneNumberId = @WhatsAppPhoneNumberId;

IF @WhatsAppNumberId IS NULL
BEGIN
    INSERT INTO dbo.BusinessWhatsAppNumbers
        (BusinessWhatsAppNumberId, BusinessId, AgentId, PhoneNumber,
         WhatsAppBusinessAccountId, WhatsAppPhoneNumberId, WhatsAppAccessToken,
         IsActive, CreatedAt)
    VALUES
        (@CJWhatsAppNumberId, @CJBusinessId, @CJAgentId, @PhoneNumber,
         @WhatsAppBusinessAccountId, @WhatsAppPhoneNumberId, COALESCE(@ConfiguredAccessToken, N''),
         CASE WHEN @ConfiguredAccessToken IS NULL THEN 0 ELSE 1 END, GETUTCDATE());

    SET @WhatsAppNumberId = @CJWhatsAppNumberId;
    IF @ConfiguredAccessToken IS NULL
    BEGIN
        PRINT N'MigrateDigitalShopWhatsAppToCJ: identificadores Meta preparados en CJ; canal inactivo hasta cargar un access token valido.';
        RETURN;
    END
END

IF NOT EXISTS (
    SELECT 1
    FROM dbo.BusinessWhatsAppNumbers
    WHERE BusinessWhatsAppNumberId = @WhatsAppNumberId
      AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
)
BEGIN
    UPDATE dbo.BusinessWhatsAppNumbers
    SET BusinessId = @CJBusinessId,
        AgentId = @CJAgentId,
        PhoneNumber = @PhoneNumber,
        WhatsAppBusinessAccountId = @WhatsAppBusinessAccountId,
        WhatsAppAccessToken = COALESCE(@ConfiguredAccessToken, WhatsAppAccessToken),
        IsActive = CASE WHEN @ConfiguredAccessToken IS NULL THEN 0 ELSE 1 END
    WHERE BusinessWhatsAppNumberId = @WhatsAppNumberId;

    IF @ConfiguredAccessToken IS NULL
    BEGIN
        PRINT N'MigrateDigitalShopWhatsAppToCJ: identificadores Meta actualizados en CJ; canal inactivo hasta cargar un access token valido.';
        RETURN;
    END
END

BEGIN TRANSACTION;

UPDATE dbo.BusinessWhatsAppNumbers
SET IsActive = 0
WHERE BusinessId = @CJBusinessId
  AND BusinessWhatsAppNumberId <> @WhatsAppNumberId
  AND IsActive = 1;

UPDATE dbo.BusinessWhatsAppNumbers
SET BusinessId = @CJBusinessId,
    AgentId = @CJAgentId,
    PhoneNumber = @PhoneNumber,
    WhatsAppPhoneNumberId = @WhatsAppPhoneNumberId,
    WhatsAppBusinessAccountId = @WhatsAppBusinessAccountId,
    WhatsAppAccessToken = COALESCE(@ConfiguredAccessToken, WhatsAppAccessToken),
    IsActive = 1
WHERE BusinessWhatsAppNumberId = @WhatsAppNumberId;

IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, 'MigrateDigitalShopWhatsAppToCJ: no se pudo reasignar el canal.', 1;
END

IF EXISTS (
    SELECT 1
    FROM dbo.IntegrationConnections
    WHERE IntegrationConnectionId = @CJCommerceConnectionId
      AND BusinessId = @CJBusinessId
)
BEGIN
    MERGE dbo.IntegrationChannelWarehouses AS target
    USING (SELECT
        @CJBusinessId AS BusinessId,
        @CJCommerceConnectionId AS IntegrationConnectionId,
        @WhatsAppNumberId AS BusinessWhatsAppNumberId
    ) AS source
    ON target.IntegrationConnectionId = source.IntegrationConnectionId
       AND target.BusinessWhatsAppNumberId = source.BusinessWhatsAppNumberId
    WHEN MATCHED THEN UPDATE SET
        BusinessId = source.BusinessId,
        WarehouseCode = N'2',
        WarehouseName = N'SAN MARTIN',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHEN NOT MATCHED THEN INSERT
        (IntegrationChannelWarehouseId, BusinessId, IntegrationConnectionId, BusinessWhatsAppNumberId,
         WarehouseCode, WarehouseName, IsActive, CreatedAt)
    VALUES
        (NEWID(), source.BusinessId, source.IntegrationConnectionId, source.BusinessWhatsAppNumberId,
         N'2', N'SAN MARTIN', 1, GETUTCDATE());
END

COMMIT TRANSACTION;

PRINT N'MigrateDigitalShopWhatsAppToCJ: canal activo reasignado a CJ Distribuciones.';
