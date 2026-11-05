-- Initial DEV channel provisioning only. Existing assignments, credentials and
-- activation state belong to channel administration, not deployment replay.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF LOWER(N'$(DeploymentEnvironment)') = N'prod'
BEGIN
    PRINT N'SeedCJWhatsAppNumber: configuración inicial de demostración omitida en producción.';
    RETURN;
END;

DECLARE @CJBusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @CJAgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000020';
DECLARE @CJCommerceConnectionId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000030';
DECLARE @CJWhatsAppNumberId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000050';
DECLARE @WhatsAppPhoneNumberId NVARCHAR(100) = N'1234810033044432';
DECLARE @ConfiguredAccessToken NVARCHAR(500) = NULLIF(LTRIM(RTRIM(N'$(CJWhatsAppAccessToken)')), N'');

BEGIN TRANSACTION;

-- Check all existing identities before provisioning; never reassign a channel
-- or overwrite a configuration merely because its phone ID was edited.
IF EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
           WHERE BusinessId = @CJBusinessId)
   OR EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
              WHERE BusinessWhatsAppNumberId = @CJWhatsAppNumberId)
   OR EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
              WHERE WhatsAppPhoneNumberId = @WhatsAppPhoneNumberId)
BEGIN
    COMMIT TRANSACTION;
    PRINT N'SeedCJWhatsAppNumber: configuración existente conservada; no se repiten traslados históricos.';
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Agents
               WHERE AgentId = @CJAgentId AND BusinessId = @CJBusinessId AND IsActive = 1)
    THROW 51000, 'SeedCJWhatsAppNumber: agente CJ activo no encontrado.', 1;

INSERT dbo.BusinessWhatsAppNumbers
    (BusinessWhatsAppNumberId, BusinessId, AgentId, PhoneNumber,
     WhatsAppBusinessAccountId, WhatsAppPhoneNumberId, WhatsAppAccessToken, IsActive, CreatedAt)
VALUES
    (@CJWhatsAppNumberId, @CJBusinessId, @CJAgentId, N'573117323198',
     N'4841200399440958', @WhatsAppPhoneNumberId, COALESCE(@ConfiguredAccessToken, N''),
     CASE WHEN @ConfiguredAccessToken IS NULL THEN 0 ELSE 1 END, GETUTCDATE());

IF @ConfiguredAccessToken IS NOT NULL AND EXISTS (
    SELECT 1 FROM dbo.IntegrationConnections
    WHERE IntegrationConnectionId = @CJCommerceConnectionId AND BusinessId = @CJBusinessId)
BEGIN
    INSERT dbo.IntegrationChannelWarehouses
        (IntegrationChannelWarehouseId, BusinessId, IntegrationConnectionId, BusinessWhatsAppNumberId,
         WarehouseCode, WarehouseName, IsActive, CreatedAt)
    VALUES
        (NEWID(), @CJBusinessId, @CJCommerceConnectionId, @CJWhatsAppNumberId,
         N'2', N'SAN MARTIN', 1, GETUTCDATE());
END;

COMMIT TRANSACTION;

IF @ConfiguredAccessToken IS NULL
    PRINT N'SeedCJWhatsAppNumber: canal preparado e inactivo hasta cargar un access token valido.';
ELSE
    PRINT N'SeedCJWhatsAppNumber: configuración inicial creada con el token suministrado.';
