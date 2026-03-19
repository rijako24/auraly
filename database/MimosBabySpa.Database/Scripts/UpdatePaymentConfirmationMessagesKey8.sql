-- ============================================================
-- DEPRECATED — no ejecutar después de migración 020_NodeConfigMigration.sql
--
-- Key=8 (PaymentConfirmationMessages) fue eliminado de BusinessConfigurations
-- en la migración 020. Los mensajes de confirmación de pago ahora deben
-- configurarse en el config del nodo correspondiente del FlowDefinition.
--
-- El attachment (indicaciones-para-tu-visita.pdf) puede seguir insertándose
-- en BusinessAttachments de forma independiente si es necesario.
-- ============================================================

SET NOCOUNT ON;

DECLARE @BusinessId   UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @AttachmentId UNIQUEIDENTIFIER = '8a1ec489-f1ba-4c7c-9576-382dfc9a55f1';

-- El attachment sigue siendo válido — solo ya no se referencia desde BusinessConfigurations.
IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessAttachmentId] = @AttachmentId)
BEGIN
    INSERT INTO [dbo].[BusinessAttachments] (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES (@AttachmentId, @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones', 1, GETUTCDATE());
    PRINT N'Attachment creado: ' + CAST(@AttachmentId AS NVARCHAR(50));
END
ELSE
    PRINT N'Attachment ya existe: ' + CAST(@AttachmentId AS NVARCHAR(50));

-- Key=8 ya no existe — no se inserta ni actualiza BusinessConfigurations.
PRINT N'UpdatePaymentConfirmationMessagesKey8: BusinessConfigurations key=8 omitido (deprecated en migración 020).';
GO
