-- =============================================================================
-- MigrateMedidentalWhatsAppToDigitalShop.sql
--
-- Reasigna a Digital Shop el canal activo que pertenecia a Medidental.
-- Conserva las credenciales existentes sin imprimirlas ni duplicarlas.
-- =============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @MedidentalBusinessId UNIQUEIDENTIFIER = 'D3E4A700-0000-0000-0000-000000000010';
DECLARE @DigitalShopBusinessId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000010';
DECLARE @DigitalShopAgentId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000020';
DECLARE @WhatsAppNumberId UNIQUEIDENTIFIER;

IF NOT EXISTS (
    SELECT 1 FROM dbo.Agents
    WHERE AgentId = @DigitalShopAgentId
      AND BusinessId = @DigitalShopBusinessId
      AND IsActive = 1)
BEGIN
    PRINT N'MigrateMedidentalWhatsAppToDigitalShop: agente Digital Shop no encontrado; migracion omitida.';
    RETURN;
END

SELECT TOP (1)
    @WhatsAppNumberId = BusinessWhatsAppNumberId
FROM dbo.BusinessWhatsAppNumbers
WHERE BusinessId = @MedidentalBusinessId
  AND IsActive = 1
ORDER BY CreatedAt DESC;

IF @WhatsAppNumberId IS NULL
BEGIN
    PRINT N'MigrateMedidentalWhatsAppToDigitalShop: Medidental no tiene un numero activo; migracion omitida.';
    RETURN;
END

BEGIN TRANSACTION;

UPDATE dbo.BusinessWhatsAppNumbers
SET IsActive = 0
WHERE BusinessId = @DigitalShopBusinessId
  AND BusinessWhatsAppNumberId <> @WhatsAppNumberId
  AND IsActive = 1;

UPDATE dbo.BusinessWhatsAppNumbers
SET BusinessId = @DigitalShopBusinessId,
    AgentId = @DigitalShopAgentId,
    IsActive = 1
WHERE BusinessWhatsAppNumberId = @WhatsAppNumberId;

IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, 'MigrateMedidentalWhatsAppToDigitalShop: no se pudo reasignar el numero.', 1;
END

COMMIT TRANSACTION;

PRINT N'MigrateMedidentalWhatsAppToDigitalShop: numero activo reasignado a Catalina (Digital Shop).';
