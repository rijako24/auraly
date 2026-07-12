-- =============================================================================
-- MigrateLuisWhatsAppToCJ.sql
--
-- Transfiere a CJ Distribuciones la configuracion WhatsApp real de Luis Petit
-- sin copiar ni revelar credenciales. Conserva otros numeros de CJ inactivos
-- para permitir rollback operacional. Idempotente.
-- =============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LuisBusinessId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000001';
DECLARE @CJBusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @CJAgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000020';
DECLARE @LuisPhoneNumber NVARCHAR(20) = N'+573117323198';
DECLARE @LuisWhatsAppPhoneId NVARCHAR(100) = N'1234810033044432';
DECLARE @WhatsAppNumberId UNIQUEIDENTIFIER;

BEGIN TRANSACTION;

SELECT TOP (1)
    @WhatsAppNumberId = BusinessWhatsAppNumberId
FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
WHERE BusinessId = @LuisBusinessId
  AND IsActive = 1
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY CreatedAt DESC;

IF @WhatsAppNumberId IS NULL
BEGIN
    SELECT TOP (1)
        @WhatsAppNumberId = BusinessWhatsAppNumberId
    FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessId = @CJBusinessId
      AND (WhatsAppPhoneNumberId = @LuisWhatsAppPhoneId OR PhoneNumber = @LuisPhoneNumber)
      AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
    ORDER BY IsActive DESC, CreatedAt DESC;
END

IF @WhatsAppNumberId IS NOT NULL
BEGIN
    UPDATE dbo.BusinessWhatsAppNumbers
    SET IsActive = 0
    WHERE BusinessId = @CJBusinessId
      AND BusinessWhatsAppNumberId <> @WhatsAppNumberId
      AND IsActive = 1;

    UPDATE dbo.BusinessWhatsAppNumbers
    SET BusinessId = @CJBusinessId,
        AgentId = @CJAgentId,
        IsActive = 1
    WHERE BusinessWhatsAppNumberId = @WhatsAppNumberId;

    PRINT N'MigrateLuisWhatsAppToCJ: numero WhatsApp transferido o confirmado para CJ.';
END
ELSE
BEGIN
    PRINT N'MigrateLuisWhatsAppToCJ: Luis no tiene una configuracion WhatsApp transferible en este entorno; CJ no fue modificado.';
END

UPDATE dbo.Agents
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @LuisBusinessId
  AND IsActive = 1;

UPDATE dbo.BusinessInboundContacts
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @LuisBusinessId
  AND IsActive = 1;

UPDATE dbo.Businesses
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @LuisBusinessId
  AND IsActive = 1;

COMMIT TRANSACTION;