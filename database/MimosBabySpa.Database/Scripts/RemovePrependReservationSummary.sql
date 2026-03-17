-- ============================================================
-- Script: RemovePrependReservationSummary
-- Elimina prependReservationSummary del JSON de PaymentConfirmationMessages (Key=8).
-- Ejecutar en SSMS o: sqlcmd -S .\LOCAL -d talkioai -U admin -P masterkey -i RemovePrependReservationSummary.sql
-- ============================================================

SET NOCOUNT ON;

-- Quitar "prependReservationSummary" del JSON
UPDATE [dbo].[BusinessConfigurations]
SET [Value] = REPLACE(REPLACE(REPLACE(
    [Value],
    N'"prependReservationSummary": true,', N''),
    N'"prependReservationSummary": false,', N''),
    N'"prependReservationSummary":false,', N'')),
    [UpdatedAt] = GETUTCDATE()
WHERE [Key] = 8
  AND [Value] LIKE N'%prependReservationSummary%';

DECLARE @Updated INT = @@ROWCOUNT;
PRINT N'Actualizado: ' + CAST(@Updated AS NVARCHAR(10)) + N' configuración(es).';
GO
