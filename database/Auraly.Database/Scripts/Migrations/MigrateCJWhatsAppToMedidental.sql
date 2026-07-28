-- =============================================================================
-- MigrateCJWhatsAppToMedidental.sql
--
-- Configura el canal de WhatsApp de Medidental reutilizando, sin revelar, el
-- token activo de CJ Distribuciones. Idempotente y seguro de re-ejecutar.
-- =============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CJBusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @MedidentalBusinessId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000010';
DECLARE @MedidentalAgentId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000020';
DECLARE @PhoneNumber NVARCHAR(20) = N'+573007047440';
DECLARE @WhatsAppPhoneId NVARCHAR(100) = N'1256148054246934';
DECLARE @AccessToken NVARCHAR(500);
DECLARE @WhatsAppBusinessAccountId NVARCHAR(100);
DECLARE @ExistingWhatsAppNumberId UNIQUEIDENTIFIER;

SELECT TOP (1)
    @AccessToken = WhatsAppAccessToken,
    @WhatsAppBusinessAccountId = WhatsAppBusinessAccountId
FROM dbo.BusinessWhatsAppNumbers
WHERE BusinessId = @CJBusinessId
  AND IsActive = 1
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY CreatedAt DESC;

IF @AccessToken IS NULL
BEGIN
    THROW 51000, 'MigrateCJWhatsAppToMedidental: CJ no tiene un token activo para copiar.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @MedidentalBusinessId)
BEGIN
    THROW 51000, 'MigrateCJWhatsAppToMedidental: negocio Medidental no encontrado.', 1;
END

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Agents
    WHERE AgentId = @MedidentalAgentId
      AND BusinessId = @MedidentalBusinessId
)
BEGIN
    THROW 51000, 'MigrateCJWhatsAppToMedidental: agente Medidental no encontrado.', 1;
END

BEGIN TRANSACTION;

SELECT TOP (1)
    @ExistingWhatsAppNumberId = BusinessWhatsAppNumberId
FROM dbo.BusinessWhatsAppNumbers WITH (UPDLOCK, HOLDLOCK)
WHERE WhatsAppPhoneNumberId = @WhatsAppPhoneId
   OR BusinessId = @MedidentalBusinessId
ORDER BY
    CASE WHEN WhatsAppPhoneNumberId = @WhatsAppPhoneId THEN 0 ELSE 1 END,
    IsActive DESC,
    CreatedAt DESC;

UPDATE dbo.BusinessWhatsAppNumbers
SET IsActive = 0
WHERE BusinessId = @MedidentalBusinessId
  AND (@ExistingWhatsAppNumberId IS NULL
       OR BusinessWhatsAppNumberId <> @ExistingWhatsAppNumberId)
  AND IsActive = 1;

IF @ExistingWhatsAppNumberId IS NULL
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
        @MedidentalBusinessId,
        @MedidentalAgentId,
        @PhoneNumber,
        @WhatsAppBusinessAccountId,
        @WhatsAppPhoneId,
        @AccessToken,
        1,
        GETUTCDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.BusinessWhatsAppNumbers
    SET BusinessId = @MedidentalBusinessId,
        AgentId = @MedidentalAgentId,
        PhoneNumber = @PhoneNumber,
        WhatsAppBusinessAccountId = COALESCE(WhatsAppBusinessAccountId, @WhatsAppBusinessAccountId),
        WhatsAppPhoneNumberId = @WhatsAppPhoneId,
        WhatsAppAccessToken = @AccessToken,
        IsActive = 1
    WHERE BusinessWhatsAppNumberId = @ExistingWhatsAppNumberId;
END

COMMIT TRANSACTION;

PRINT N'MigrateCJWhatsAppToMedidental: WhatsApp configurado para Medidental.';