SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
-- =============================================================================
-- ResetSolorzanoOrdersPayments.sql
--
-- Limpieza puntual de datos transaccionales de Vinos Artesanales Solorzano.
-- Borra conversaciones, mensajes, pedidos, pagos y uso acumulado del negocio,
-- y reinicia la suscripcion activa para que el periodo empiece hoy en cero.
-- No borra configuracion, agentes, catalogo, productos, contactos ni planes.
-- =============================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @TodayUtc DATETIME2(0) = CONVERT(DATETIME2(0), CONVERT(DATE, SYSUTCDATETIME()));
DECLARE @NextPeriodUtc DATETIME2(0) = DATEADD(MONTH, 1, @TodayUtc);

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    THROW 51001, 'ResetSolorzanoOrdersPayments: negocio Solorzano no encontrado.', 1;
END

CREATE TABLE #ConversationIds (ConversationId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
CREATE TABLE #OrderIds (OrderId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
CREATE TABLE #OrderDraftIds (OrderDraftId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
CREATE TABLE #PaymentTransactionIds (PaymentTransactionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
CREATE TABLE #ExternalAttemptIds (ExternalEscalationAttemptId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
CREATE TABLE #ActiveSubscriptionIds (BusinessSubscriptionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);

BEGIN TRANSACTION;

INSERT INTO #ConversationIds (ConversationId)
SELECT ConversationId
FROM dbo.Conversations
WHERE BusinessId = @BusinessId;

INSERT INTO #OrderIds (OrderId)
SELECT OrderId
FROM dbo.Orders
WHERE BusinessId = @BusinessId;

IF OBJECT_ID(N'dbo.OrderDrafts', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        INSERT INTO #OrderDraftIds (OrderDraftId)
        SELECT OrderDraftId
        FROM dbo.OrderDrafts
        WHERE BusinessId = @BusinessId;',
        N'@BusinessId UNIQUEIDENTIFIER', @BusinessId;
END

INSERT INTO #PaymentTransactionIds (PaymentTransactionId)
SELECT PaymentTransactionId
FROM dbo.PaymentTransactions
WHERE BusinessId = @BusinessId;

INSERT INTO #ExternalAttemptIds (ExternalEscalationAttemptId)
SELECT ExternalEscalationAttemptId
FROM dbo.ExternalEscalationAttempts
WHERE BusinessId = @BusinessId
  AND (
      EventName LIKE N'order%'
      OR TargetType = N'order'
      OR EXISTS (SELECT 1 FROM #OrderIds o WHERE o.OrderId = TargetId)
  );

INSERT INTO #ActiveSubscriptionIds (BusinessSubscriptionId)
SELECT BusinessSubscriptionId
FROM dbo.BusinessSubscriptions
WHERE BusinessId = @BusinessId
  AND Status = 1;

DELETE ule
FROM dbo.UsageLedgerEntries ule
WHERE ule.BusinessId = @BusinessId;

DELETE imr
FROM dbo.InboundMessageReceipts imr
WHERE imr.BusinessId = @BusinessId;

DELETE cm
FROM dbo.CustomerMemory cm
WHERE cm.BusinessId = @BusinessId;

DELETE pa
FROM dbo.PromotionApplications pa
WHERE pa.BusinessId = @BusinessId
  AND (
      EXISTS (SELECT 1 FROM #OrderIds o WHERE o.OrderId = pa.OrderId)
      OR EXISTS (SELECT 1 FROM #PaymentTransactionIds pt WHERE pt.PaymentTransactionId = pa.PaymentTransactionId)
  );

IF OBJECT_ID(N'dbo.OrderDrafts', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE od
        SET PaymentTransactionId = NULL,
            UpdatedAt = SYSUTCDATETIME()
        FROM dbo.OrderDrafts od
        WHERE od.BusinessId = @BusinessId
          AND od.PaymentTransactionId IS NOT NULL;',
        N'@BusinessId UNIQUEIDENTIFIER', @BusinessId;
END

UPDATE o
SET PaymentTransactionId = NULL,
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.Orders o
WHERE o.BusinessId = @BusinessId
  AND o.PaymentTransactionId IS NOT NULL;

UPDATE pt
SET SupersededByPaymentTransactionId = NULL
FROM dbo.PaymentTransactions pt
WHERE pt.BusinessId = @BusinessId
   OR pt.SupersededByPaymentTransactionId IN (
        SELECT PaymentTransactionId FROM #PaymentTransactionIds
   );

IF OBJECT_ID(N'dbo.OrderDraftItems', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        DELETE odi
        FROM dbo.OrderDraftItems odi
        WHERE EXISTS (
            SELECT 1
            FROM #OrderDraftIds ids
            WHERE ids.OrderDraftId = odi.OrderDraftId
        );';
END

IF OBJECT_ID(N'dbo.OrderDrafts', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        DELETE od
        FROM dbo.OrderDrafts od
        WHERE EXISTS (
            SELECT 1
            FROM #OrderDraftIds ids
            WHERE ids.OrderDraftId = od.OrderDraftId
        );';
END

DELETE oce
FROM dbo.OrderConnectionEvents oce
WHERE EXISTS (SELECT 1 FROM #OrderIds o WHERE o.OrderId = oce.OrderId);

DELETE oi
FROM dbo.OrderItems oi
WHERE EXISTS (SELECT 1 FROM #OrderIds o WHERE o.OrderId = oi.OrderId);

DELETE e
FROM dbo.Enrollments e
WHERE e.BusinessId = @BusinessId
  AND (
      EXISTS (SELECT 1 FROM #ConversationIds c WHERE c.ConversationId = e.ConversationId)
      OR EXISTS (SELECT 1 FROM #PaymentTransactionIds pt WHERE pt.PaymentTransactionId = e.PaymentTransactionId)
  );

DELETE o
FROM dbo.Orders o
WHERE EXISTS (SELECT 1 FROM #OrderIds ids WHERE ids.OrderId = o.OrderId);

DELETE eea
FROM dbo.ExternalEscalationAttempts eea
WHERE EXISTS (SELECT 1 FROM #ExternalAttemptIds ids WHERE ids.ExternalEscalationAttemptId = eea.ExternalEscalationAttemptId);

DELETE pt
FROM dbo.PaymentTransactions pt
WHERE EXISTS (SELECT 1 FROM #PaymentTransactionIds ids WHERE ids.PaymentTransactionId = pt.PaymentTransactionId);

DELETE bup
FROM dbo.BusinessUsagePeriods bup
WHERE bup.BusinessId = @BusinessId;

UPDATE bs
SET CurrentPeriodStart = @TodayUtc,
    CurrentPeriodEnd = @NextPeriodUtc,
    ExtraCredits = 0,
    ExtraVariableCostCop = 0,
    Status = 1,
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.BusinessSubscriptions bs
WHERE bs.BusinessId = @BusinessId
  AND bs.Status = 1;

INSERT INTO dbo.BusinessUsagePeriods
    (BusinessSubscriptionId, BusinessId, PeriodStart, PeriodEnd,
     CreditsIncluded, CreditsExtra, CreditsUsed,
     VariableCostLimitCop, VariableCostExtraCop, VariableCostUsedCop,
     Status, ExceededAt, CreatedAt, UpdatedAt)
SELECT bs.BusinessSubscriptionId,
       bs.BusinessId,
       bs.CurrentPeriodStart,
       bs.CurrentPeriodEnd,
       bs.IncludedCredits,
       0,
       0,
       bs.MaxVariableCostCop,
       0,
       0,
       1,
       NULL,
       SYSUTCDATETIME(),
       SYSUTCDATETIME()
FROM dbo.BusinessSubscriptions bs
WHERE bs.BusinessId = @BusinessId
  AND bs.Status = 1;

DELETE c
FROM dbo.Conversations c
WHERE EXISTS (SELECT 1 FROM #ConversationIds ids WHERE ids.ConversationId = c.ConversationId);

DECLARE @ConversationCount INT = (SELECT COUNT(*) FROM #ConversationIds);
DECLARE @OrderCount INT = (SELECT COUNT(*) FROM #OrderIds);
DECLARE @PaymentCount INT = (SELECT COUNT(*) FROM #PaymentTransactionIds);
DECLARE @SubscriptionCount INT = (SELECT COUNT(*) FROM #ActiveSubscriptionIds);

COMMIT TRANSACTION;

PRINT N'ResetSolorzanoOrdersPayments: limpieza completa de Solorzano aplicada.';
PRINT N'Conversaciones eliminadas: ' + CAST(@ConversationCount AS NVARCHAR(20));
PRINT N'Pedidos eliminados: ' + CAST(@OrderCount AS NVARCHAR(20));
PRINT N'Pagos eliminados: ' + CAST(@PaymentCount AS NVARCHAR(20));
PRINT N'Suscripciones activas reiniciadas: ' + CAST(@SubscriptionCount AS NVARCHAR(20));
PRINT N'Nuevo inicio de periodo UTC: ' + CONVERT(NVARCHAR(30), @TodayUtc, 126);
