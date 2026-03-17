-- ============================================================
-- Script: UpdatePaymentConfirmationMessagesKey8
-- Actualiza Key=8 con messages que tienen body y attachmentId.
-- Crea el attachment si no existe (BlobPath relativo: confirmations/indicaciones-para-tu-visita.pdf).
-- ============================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @AttachmentId UNIQUEIDENTIFIER = '8a1ec489-f1ba-4c7c-9576-382dfc9a55f1';

-- Crear attachment si no existe
IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessAttachmentId] = @AttachmentId)
    INSERT INTO [dbo].[BusinessAttachments] (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES (@AttachmentId, @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones', 1, GETUTCDATE());
DECLARE @Value NVARCHAR(MAX) = N'{
  "messages": [
    {"body": "✅ ¡Tu pago ha sido confirmado y tu reserva creada!"},
    {"body": "Estos son los términos y condiciones:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1"}
  ]
}';

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = @Value, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [Key] = 8;

IF @@ROWCOUNT = 0
    INSERT INTO [dbo].[BusinessConfigurations] (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, 8, @Value, N'Mensajes confirmación pago. body y opcional attachmentId.', 1, GETUTCDATE());

PRINT N'PaymentConfirmationMessages (Key=8) actualizado.';
GO
