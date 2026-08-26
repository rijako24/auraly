-- =============================================================================
-- MigrateDigitalShopWhatsAppToCJ.sql
--
-- Reasigna a CJ Distribuciones el canal real que estaba operando Digital Shop.
-- Conserva las credenciales existentes sin imprimirlas ni duplicarlas.
-- =============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DigitalShopBusinessId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000010';
DECLARE @CJBusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @CJAgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000020';
DECLARE @CJCommerceConnectionId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000030';
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
WHERE BusinessId IN (@DigitalShopBusinessId, @CJBusinessId)
  AND IsActive = 1
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY
    CASE WHEN BusinessId = @CJBusinessId THEN 0 ELSE 1 END,
    CreatedAt DESC;

IF @WhatsAppNumberId IS NULL
BEGIN
    PRINT N'MigrateDigitalShopWhatsAppToCJ: no existe un canal activo transferible; configura el canal de CJ por la administracion autorizada.';
    RETURN;
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
