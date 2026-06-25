SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
-- =============================================================================
-- ResetSolorzanoOrdersPayments.sql
--
-- Limpieza puntual de datos transaccionales de Vinos Artesanales Solorzano para
-- empezar pedidos desde cero sin borrar configuracion, agentes, contactos ni
-- conversaciones historicas.
-- =============================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    THROW 51001, 'ResetSolorzanoOrdersPayments: negocio Solorzano no encontrado.', 1;
END

BEGIN TRANSACTION;

DECLARE @OrderIds TABLE (OrderId UNIQUEIDENTIFIER PRIMARY KEY);
DECLARE @OrderDraftIds TABLE (OrderDraftId UNIQUEIDENTIFIER PRIMARY KEY);
DECLARE @PaymentTransactionIds TABLE (PaymentTransactionId UNIQUEIDENTIFIER PRIMARY KEY);
DECLARE @ExternalAttemptIds TABLE (ExternalEscalationAttemptId UNIQUEIDENTIFIER PRIMARY KEY);

INSERT INTO @OrderIds (OrderId)
SELECT OrderId
FROM dbo.Orders
WHERE BusinessId = @BusinessId;

INSERT INTO @OrderDraftIds (OrderDraftId)
SELECT OrderDraftId
FROM dbo.OrderDrafts
WHERE BusinessId = @BusinessId;

INSERT INTO @PaymentTransactionIds (PaymentTransactionId)
SELECT PaymentTransactionId
FROM dbo.PaymentTransactions
WHERE BusinessId = @BusinessId;

INSERT INTO @ExternalAttemptIds (ExternalEscalationAttemptId)
SELECT ExternalEscalationAttemptId
FROM dbo.ExternalEscalationAttempts
WHERE BusinessId = @BusinessId
  AND (
      EventName LIKE N'order%'
      OR TargetType = N'order'
      OR EXISTS (SELECT 1 FROM @OrderIds o WHERE o.OrderId = TargetId)
  );

UPDATE od
SET PaymentTransactionId = NULL,
    UpdatedAt = GETUTCDATE()
FROM dbo.OrderDrafts od
WHERE od.BusinessId = @BusinessId
  AND od.PaymentTransactionId IS NOT NULL;

UPDATE o
SET PaymentTransactionId = NULL,
    DeliveryExternalEscalationAttemptId = NULL,
    UpdatedAt = GETUTCDATE()
FROM dbo.Orders o
WHERE o.BusinessId = @BusinessId
  AND (o.PaymentTransactionId IS NOT NULL OR o.DeliveryExternalEscalationAttemptId IS NOT NULL);

UPDATE pt
SET SupersededByPaymentTransactionId = NULL
FROM dbo.PaymentTransactions pt
WHERE pt.SupersededByPaymentTransactionId IN (
    SELECT PaymentTransactionId FROM @PaymentTransactionIds
);

DELETE pa
FROM dbo.PromotionApplications pa
WHERE pa.BusinessId = @BusinessId
  AND (
      EXISTS (SELECT 1 FROM @OrderIds o WHERE o.OrderId = pa.OrderId)
      OR EXISTS (SELECT 1 FROM @PaymentTransactionIds pt WHERE pt.PaymentTransactionId = pa.PaymentTransactionId)
  );

DELETE odi
FROM dbo.OrderDraftItems odi
WHERE EXISTS (SELECT 1 FROM @OrderDraftIds od WHERE od.OrderDraftId = odi.OrderDraftId);

DELETE od
FROM dbo.OrderDrafts od
WHERE EXISTS (SELECT 1 FROM @OrderDraftIds ids WHERE ids.OrderDraftId = od.OrderDraftId);

DELETE oce
FROM dbo.OrderConnectionEvents oce
WHERE EXISTS (SELECT 1 FROM @OrderIds o WHERE o.OrderId = oce.OrderId);

DELETE oi
FROM dbo.OrderItems oi
WHERE EXISTS (SELECT 1 FROM @OrderIds o WHERE o.OrderId = oi.OrderId);

DELETE o
FROM dbo.Orders o
WHERE EXISTS (SELECT 1 FROM @OrderIds ids WHERE ids.OrderId = o.OrderId);

DELETE eea
FROM dbo.ExternalEscalationAttempts eea
WHERE EXISTS (SELECT 1 FROM @ExternalAttemptIds ids WHERE ids.ExternalEscalationAttemptId = eea.ExternalEscalationAttemptId);

DELETE e
FROM dbo.Enrollments e
WHERE e.BusinessId = @BusinessId
  AND EXISTS (SELECT 1 FROM @PaymentTransactionIds pt WHERE pt.PaymentTransactionId = e.PaymentTransactionId);

DELETE pt
FROM dbo.PaymentTransactions pt
WHERE EXISTS (SELECT 1 FROM @PaymentTransactionIds ids WHERE ids.PaymentTransactionId = pt.PaymentTransactionId);

COMMIT TRANSACTION;

PRINT N'ResetSolorzanoOrdersPayments: pedidos, borradores, pagos y asignaciones externas de Solorzano eliminados.';
