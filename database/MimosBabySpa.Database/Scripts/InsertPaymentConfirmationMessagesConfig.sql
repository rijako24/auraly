-- ============================================================
-- DEPRECATED — no ejecutar después de migración 020_NodeConfigMigration.sql
--
-- Key=8 (PaymentConfirmationMessages) fue eliminado de BusinessConfigurations
-- en la migración 020. Los mensajes de confirmación de pago ahora deben
-- configurarse en el config del nodo correspondiente del FlowDefinition.
-- ============================================================
PRINT N'InsertPaymentConfirmationMessagesConfig: script deprecated, no action taken.';
GO
