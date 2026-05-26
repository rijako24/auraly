-- =============================================================================
-- MigrateMessagesTextWhatsAppLimit.sql
--
-- Expande Messages.MessageText para soportar el límite de WhatsApp (4096 chars).
-- SQL Server no admite NVARCHAR(n) con n > 4000; usamos NVARCHAR(MAX) y el tope
-- se aplica en aplicación (MessageService / WhatsAppMessageLimits).
-- Idempotente: solo altera si la columna no es ya MAX.
-- =============================================================================

DECLARE @IsMax BIT = 0;

SELECT @IsMax = CASE WHEN c.max_length = -1 THEN 1 ELSE 0 END
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID(N'dbo.Messages')
  AND  c.name      = N'MessageText';

IF @IsMax = 0
BEGIN
    ALTER TABLE dbo.Messages
    ALTER COLUMN [MessageText] NVARCHAR(MAX) NOT NULL;

    PRINT 'Messages.MessageText expanded to NVARCHAR(MAX) (app enforces WhatsApp 4096 limit).';
END
ELSE
BEGIN
    PRINT 'Messages.MessageText already NVARCHAR(MAX) — skipping.';
END
