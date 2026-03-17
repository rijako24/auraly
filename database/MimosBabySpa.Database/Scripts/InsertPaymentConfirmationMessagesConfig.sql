-- ============================================================
-- Script: InsertPaymentConfirmationMessagesConfig
-- Inserta la configuración PaymentConfirmationMessages (Key=8) para todos los negocios.
-- JSON: mensajes enviados al cliente cuando se confirma el pago.
-- Cada mensaje tiene body (texto) y opcionalmente attachmentId (GUID de BusinessAttachments).
-- Ejecutar: sqlcmd -S server -d MimosBabySpa -i "InsertPaymentConfirmationMessagesConfig.sql"
-- ============================================================

SET NOCOUNT ON;

-- attachmentId es opcional; si se especifica, referencia BusinessAttachments.
DECLARE @DefaultValue NVARCHAR(MAX) = N'{
  "messages": [
    {
      "body": "✅ ¡Tu pago ha sido confirmado y tu reserva creada!"
    }
  ]
}';

-- Insertar para cada negocio que aún no tenga la configuración Key=8
INSERT INTO [dbo].[BusinessConfigurations] (
    [BusinessConfigurationId],
    [BusinessId],
    [Key],
    [Value],
    [Description],
    [IsActive],
    [CreatedAt]
)
SELECT
    NEWID(),
    b.[BusinessId],
    8,
    @DefaultValue,
    N'Mensajes enviados al cliente cuando se confirma el pago. body (texto) y opcional attachmentId (GUID de BusinessAttachments). Placeholders: {CustomerName}, {Service}, {Date}, {Time}, {Total}.',
    1,
    GETUTCDATE()
FROM [dbo].[Businesses] b
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[BusinessConfigurations] bc
    WHERE bc.[BusinessId] = b.[BusinessId] AND bc.[Key] = 8
);

DECLARE @Inserted INT = @@ROWCOUNT;
PRINT N'PaymentConfirmationMessages (Key=8): ' + CAST(@Inserted AS NVARCHAR(10)) + N' configuración(es) insertada(s).';
GO
