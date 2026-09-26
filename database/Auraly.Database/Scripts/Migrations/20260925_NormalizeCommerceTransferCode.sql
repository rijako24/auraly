-- One-time cutover. Stop legacy payment writes and post required closure
-- corrections with the canonical manual-voucher workflow before running.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

SELECT closure.WorkSessionClosureId,session.TenantId,session.BusinessId,
  closure.ReceiptSnapshotJson OldSnapshot,closure.ReceiptHash OldHash,
  totals.OldDifference,totals.NewDifference,
  CAST(CASE WHEN job.Status=N'Posted' OR entry.EntryId IS NOT NULL
    THEN 1 ELSE 0 END AS bit) AccountingPosted
INTO #LegacyTransferClosures
FROM dbo.WorkSessionClosures closure WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
JOIN (
  SELECT WorkSessionClosureId,SUM(COALESCE(Difference,0)) OldDifference,
    CASE WHEN COUNT(CountedAmount)=0 THEN CAST(0 AS decimal(19,4))
         ELSE SUM(COALESCE(CountedAmount,0))-SUM(NetAmount) END NewDifference
  FROM dbo.WorkSessionClosurePaymentTotals
  WHERE PaymentMethodCode IN(N'BankTransfer',N'Transfer')
  GROUP BY WorkSessionClosureId
  HAVING SUM(CASE WHEN PaymentMethodCode=N'BankTransfer' THEN 1 ELSE 0 END)=1
) totals ON totals.WorkSessionClosureId=closure.WorkSessionClosureId
LEFT JOIN dbo.AccountingPostingJobs job
  ON job.SourceDocumentId=closure.WorkSessionClosureId
 AND job.SourceDocumentType=N'WorkSessionCashDifference'
LEFT JOIN dbo.AccountingEntries entry
  ON entry.SourceDocumentId=closure.WorkSessionClosureId
 AND entry.SourceDocumentType=N'WorkSessionCashDifference';

IF EXISTS(SELECT 1 FROM #LegacyTransferClosures legacy
  JOIN dbo.WorkSessionClosureReconciliations reconciliation
    ON reconciliation.WorkSessionClosureId=legacy.WorkSessionClosureId)
  THROW 51001,N'Un cierre con transferencia anterior ya fue conciliado; requiere revisión individual.',1;

IF EXISTS(SELECT 1 FROM #LegacyTransferClosures
  WHERE ISJSON(OldSnapshot)<>1
     OR JSON_QUERY(OldSnapshot,N'$.paymentTotals') IS NULL
     OR OldHash<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),
          CONVERT(varchar(max),OldSnapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8))))
  THROW 51002,N'La huella original de un cierre no coincide; no se modificó ningún dato.',1;

IF EXISTS(SELECT 1 FROM dbo.CustomerPaymentTenders
    WHERE MethodCode=N'BankTransfer' AND BankAccountId IS NULL)
 OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentTenders
    WHERE MethodCode=N'BankTransfer' AND BankAccountId IS NULL)
  THROW 51007,N'Hay transferencias anteriores sin cuenta bancaria; requieren revisión individual.',1;

-- These sources should already use Transfer. An unexpected legacy value must
-- be reviewed rather than silently left behind by the commercial cutover.
IF EXISTS(SELECT 1 FROM dbo.SalesPayments WHERE MethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.SalesReturnSettlements WHERE MethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM reporting.SalesReportPaymentFacts WHERE MethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.WorkSessionClosureReconciliationLines
   WHERE PaymentMethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.WorkSessionClosureReclassifications
   WHERE FromPaymentMethodCode=N'BankTransfer' OR ToPaymentMethodCode=N'BankTransfer')
  THROW 51008,N'Hay otra fuente comercial con el medio anterior; requiere revisión individual.',1;

IF EXISTS(SELECT 1 FROM #LegacyTransferClosures legacy
  WHERE (SELECT COUNT(*) FROM OPENJSON(legacy.OldSnapshot,N'$.paymentTotals')
    WITH(paymentMethodCode nvarchar(32)) snapshot
    WHERE snapshot.paymentMethodCode IN(N'BankTransfer',N'Transfer'))
    <>(SELECT COUNT(*) FROM dbo.WorkSessionClosurePaymentTotals total
      WHERE total.WorkSessionClosureId=legacy.WorkSessionClosureId
        AND total.PaymentMethodCode IN(N'BankTransfer',N'Transfer')))
  THROW 51003,N'El número de medios congelados del cierre no coincide.',1;

-- The frozen snapshot and relational totals must agree before correction.
IF EXISTS(
  SELECT 1 FROM #LegacyTransferClosures legacy
  CROSS APPLY OPENJSON(legacy.OldSnapshot,N'$.paymentTotals') WITH(
    paymentMethodCode nvarchar(32),salesAmount decimal(19,4),
    refundAmount decimal(19,4),otherAmount decimal(19,4),
    netAmount decimal(19,4),countedAmount decimal(19,4),
    difference decimal(19,4)) snapshot
  WHERE snapshot.paymentMethodCode IN(N'BankTransfer',N'Transfer')
    AND NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals total
      WHERE total.WorkSessionClosureId=legacy.WorkSessionClosureId
        AND total.PaymentMethodCode=snapshot.paymentMethodCode
        AND total.SalesAmount=snapshot.salesAmount
        AND total.RefundAmount=snapshot.refundAmount
        AND total.OtherAmount=snapshot.otherAmount
        AND total.NetAmount=snapshot.netAmount
        AND (total.CountedAmount=snapshot.countedAmount
          OR total.CountedAmount IS NULL AND snapshot.countedAmount IS NULL)
        AND (total.Difference=snapshot.difference
          OR total.Difference IS NULL AND snapshot.difference IS NULL))
  UNION ALL
  SELECT 1 FROM #LegacyTransferClosures legacy
  JOIN dbo.WorkSessionClosurePaymentTotals total
    ON total.WorkSessionClosureId=legacy.WorkSessionClosureId
   AND total.PaymentMethodCode IN(N'BankTransfer',N'Transfer')
  WHERE NOT EXISTS(SELECT 1 FROM OPENJSON(legacy.OldSnapshot,N'$.paymentTotals') WITH(
      paymentMethodCode nvarchar(32),salesAmount decimal(19,4),
      refundAmount decimal(19,4),otherAmount decimal(19,4),
      netAmount decimal(19,4),countedAmount decimal(19,4),
      difference decimal(19,4)) snapshot
    WHERE snapshot.paymentMethodCode=total.PaymentMethodCode
      AND snapshot.SalesAmount=total.SalesAmount
      AND snapshot.RefundAmount=total.RefundAmount
      AND snapshot.OtherAmount=total.OtherAmount
      AND snapshot.NetAmount=total.NetAmount
      AND (snapshot.CountedAmount=total.CountedAmount
        OR snapshot.CountedAmount IS NULL AND total.CountedAmount IS NULL)
      AND (snapshot.Difference=total.Difference
        OR snapshot.Difference IS NULL AND total.Difference IS NULL)))
  THROW 51003,N'El detalle y la tirilla congelada de un cierre no coinciden.',1;

-- Posted differences need a posted, two-line manual correction. This one-time
-- check uses the account codes in the Megafruver posted entry; another PUC
-- requires an explicit review, not an unverified automatic conversion.
IF EXISTS(SELECT 1 FROM #LegacyTransferClosures legacy
  WHERE legacy.AccountingPosted=1 AND legacy.OldDifference<>legacy.NewDifference
    AND NOT EXISTS(
      SELECT 1 FROM dbo.AccountingEntries entry
      WHERE entry.BusinessId=legacy.BusinessId
        AND entry.SourceDocumentType=N'ManualAccountingVoucher'
        AND entry.Description=CONCAT(N'Corrección transferencia cierre ',
          CONVERT(nvarchar(36),legacy.WorkSessionClosureId))
        AND (SELECT COUNT(*) FROM dbo.AccountingEntries matching
          WHERE matching.BusinessId=legacy.BusinessId
            AND matching.SourceDocumentType=N'ManualAccountingVoucher'
            AND matching.Description=entry.Description)=1
        AND entry.DebitTotal=ABS(legacy.OldDifference-legacy.NewDifference)
        AND (SELECT COUNT(*) FROM dbo.AccountingEntryLines line
             WHERE line.EntryId=entry.EntryId)=2
        AND EXISTS(SELECT 1 FROM dbo.AccountingEntryLines line
          JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
          WHERE line.EntryId=entry.EntryId AND account.TenantId=legacy.TenantId
            AND account.Code=N'139995'
            AND line.Debit=CASE WHEN legacy.OldDifference>legacy.NewDifference
              THEN legacy.OldDifference-legacy.NewDifference ELSE 0 END
            AND line.Credit=CASE WHEN legacy.OldDifference<legacy.NewDifference
              THEN legacy.NewDifference-legacy.OldDifference ELSE 0 END)
        AND EXISTS(SELECT 1 FROM dbo.AccountingEntryLines line
          JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
          WHERE line.EntryId=entry.EntryId AND account.TenantId=legacy.TenantId
            AND account.Code=N'130520'
            AND line.Credit=CASE WHEN legacy.OldDifference>legacy.NewDifference
              THEN legacy.OldDifference-legacy.NewDifference ELSE 0 END
            AND line.Debit=CASE WHEN legacy.OldDifference<legacy.NewDifference
              THEN legacy.NewDifference-legacy.OldDifference ELSE 0 END)))
  THROW 51004,N'Falta el comprobante contable de corrección del cierre, ya contabilizado.',1;

SELECT legacy.WorkSessionClosureId,
  JSON_MODIFY(legacy.OldSnapshot,N'$.paymentTotals',JSON_QUERY(
    (SELECT CASE WHEN original.paymentMethodCode=N'BankTransfer'
                   THEN N'Transfer' ELSE original.paymentMethodCode END paymentMethodCode,
      SUM(original.salesAmount) salesAmount,SUM(original.refundAmount) refundAmount,
      SUM(original.otherAmount) otherAmount,SUM(original.netAmount) netAmount,
      CASE WHEN COUNT(original.countedAmount)=0 THEN CAST(NULL AS decimal(19,4))
           ELSE SUM(COALESCE(original.countedAmount,0)) END countedAmount,
      CASE WHEN COUNT(original.countedAmount)=0 THEN CAST(NULL AS decimal(19,4))
           ELSE SUM(COALESCE(original.countedAmount,0))-SUM(original.netAmount) END difference,
      CAST(MAX(CASE WHEN original.paymentMethodCode IN(N'BankTransfer',N'Transfer')
        THEN 1 ELSE original.requiresCount END) AS bit) requiresCount,
      SUM(original.cashEntryAmount) cashEntryAmount,
      SUM(original.cashExitAmount) cashExitAmount,
      SUM(original.receivableAmount) receivableAmount,
      SUM(original.payableAmount) payableAmount
    FROM OPENJSON(legacy.OldSnapshot,N'$.paymentTotals') raw
    CROSS APPLY (SELECT
      JSON_VALUE(raw.value,N'$.paymentMethodCode') paymentMethodCode,
      TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.salesAmount')) salesAmount,
      TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.refundAmount')) refundAmount,
      TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.otherAmount')) otherAmount,
      TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.netAmount')) netAmount,
      TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.countedAmount')) countedAmount,
      CASE WHEN JSON_VALUE(raw.value,N'$.requiresCount')=N'true' THEN 1 ELSE 0 END requiresCount,
      COALESCE(TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.cashEntryAmount')),0) cashEntryAmount,
      COALESCE(TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.cashExitAmount')),0) cashExitAmount,
      COALESCE(TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.receivableAmount')),0) receivableAmount,
      COALESCE(TRY_CONVERT(decimal(19,4),JSON_VALUE(raw.value,N'$.payableAmount')),0) payableAmount,
      TRY_CONVERT(int,raw.[key]) ordinal) original
    GROUP BY CASE WHEN original.paymentMethodCode=N'BankTransfer'
                    THEN N'Transfer' ELSE original.paymentMethodCode END
    ORDER BY MIN(original.ordinal)
    FOR JSON PATH,INCLUDE_NULL_VALUES))) NewSnapshot
INTO #NormalizedTransferSnapshots
FROM #LegacyTransferClosures legacy;

IF EXISTS(SELECT 1 FROM #NormalizedTransferSnapshots
  WHERE NewSnapshot IS NULL OR ISJSON(NewSnapshot)<>1
    OR EXISTS(SELECT 1 FROM OPENJSON(NewSnapshot,N'$.paymentTotals') WITH(
       paymentMethodCode nvarchar(32)) total
       WHERE total.paymentMethodCode=N'BankTransfer'))
  THROW 51005,N'No se pudo normalizar la tirilla del cierre.',1;

SELECT legacy.WorkSessionClosureId,
  SUM(total.SalesAmount) SalesAmount,SUM(total.RefundAmount) RefundAmount,
  SUM(total.OtherAmount) OtherAmount,SUM(total.NetAmount) NetAmount,
  CASE WHEN COUNT(total.CountedAmount)=0 THEN CAST(NULL AS decimal(19,4))
       ELSE SUM(COALESCE(total.CountedAmount,0)) END CountedAmount
INTO #NormalizedTransferTotals
FROM #LegacyTransferClosures legacy
JOIN dbo.WorkSessionClosurePaymentTotals total
  ON total.WorkSessionClosureId=legacy.WorkSessionClosureId
 AND total.PaymentMethodCode IN(N'BankTransfer',N'Transfer')
GROUP BY legacy.WorkSessionClosureId;

DELETE total FROM dbo.WorkSessionClosurePaymentTotals total
JOIN #LegacyTransferClosures legacy
  ON legacy.WorkSessionClosureId=total.WorkSessionClosureId
WHERE total.PaymentMethodCode IN(N'BankTransfer',N'Transfer');

INSERT dbo.WorkSessionClosurePaymentTotals(WorkSessionClosureId,PaymentMethodCode,
  SalesAmount,RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
SELECT WorkSessionClosureId,N'Transfer',SalesAmount,RefundAmount,OtherAmount,
  NetAmount,CountedAmount,
  CASE WHEN CountedAmount IS NULL THEN NULL ELSE CountedAmount-NetAmount END
FROM #NormalizedTransferTotals;

UPDATE closure
SET ReceiptSnapshotJson=snapshot.NewSnapshot,
    ReceiptHash=HASHBYTES('SHA2_256',CONVERT(varbinary(max),
      CONVERT(varchar(max),snapshot.NewSnapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8)))
FROM dbo.WorkSessionClosures closure
JOIN #NormalizedTransferSnapshots snapshot
  ON snapshot.WorkSessionClosureId=closure.WorkSessionClosureId;

INSERT dbo.AuditLogs(TenantId,BusinessId,Action,EntityType,EntityId,
  OldValues,NewValues,CorrelationId)
SELECT legacy.TenantId,legacy.BusinessId,N'NormalizeTransferPaymentMethod',
  N'WorkSessionClosure',CONVERT(nvarchar(36),legacy.WorkSessionClosureId),
  (SELECT legacy.OldSnapshot receiptSnapshotJson,
      CONVERT(varchar(64),legacy.OldHash,2) receiptHash FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT snapshot.NewSnapshot receiptSnapshotJson,
      CONVERT(varchar(64),closure.ReceiptHash,2) receiptHash FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  N'20260925-NormalizeCommerceTransferCode'
FROM #LegacyTransferClosures legacy
JOIN #NormalizedTransferSnapshots snapshot
  ON snapshot.WorkSessionClosureId=legacy.WorkSessionClosureId
JOIN dbo.WorkSessionClosures closure
  ON closure.WorkSessionClosureId=legacy.WorkSessionClosureId;

INSERT dbo.AuditLogs(TenantId,BusinessId,Action,EntityType,EntityId,
  OldValues,NewValues,CorrelationId)
SELECT business.TenantId,payment.BusinessId,N'NormalizeTransferPaymentMethod',
  N'CustomerPaymentTender',CONCAT(CONVERT(nvarchar(36),tender.PaymentId),N':',tender.LineNumber),
  (SELECT tender.MethodCode methodCode,tender.Amount amount,
      tender.BankAccountId bankAccountId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT N'Transfer' methodCode,tender.Amount amount,
      tender.BankAccountId bankAccountId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  N'20260925-NormalizeCommerceTransferCode'
FROM dbo.CustomerPaymentTenders tender
JOIN dbo.CustomerPayments payment ON payment.PaymentId=tender.PaymentId
JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
WHERE tender.MethodCode=N'BankTransfer';

INSERT dbo.AuditLogs(TenantId,BusinessId,Action,EntityType,EntityId,
  OldValues,NewValues,CorrelationId)
SELECT business.TenantId,payment.BusinessId,N'NormalizeTransferPaymentMethod',
  N'SupplierPaymentTender',CONCAT(CONVERT(nvarchar(36),tender.PaymentId),N':',tender.LineNumber),
  (SELECT tender.MethodCode methodCode,tender.Amount amount,
      tender.BankAccountId bankAccountId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT N'Transfer' methodCode,tender.Amount amount,
      tender.BankAccountId bankAccountId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  N'20260925-NormalizeCommerceTransferCode'
FROM dbo.SupplierPaymentTenders tender
JOIN dbo.SupplierPayments payment ON payment.PaymentId=tender.PaymentId
JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
WHERE tender.MethodCode=N'BankTransfer';

INSERT dbo.AuditLogs(TenantId,BusinessId,Action,EntityType,EntityId,
  OldValues,NewValues,CorrelationId)
SELECT session.TenantId,session.BusinessId,N'NormalizeTransferPaymentMethod',
  N'WorkSessionMovement',CONVERT(nvarchar(36),movement.WorkSessionMovementId),
  (SELECT movement.PaymentMethodCode paymentMethodCode,movement.Amount amount,
      movement.SourceKey sourceKey FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT N'Transfer' paymentMethodCode,movement.Amount amount,
      movement.SourceKey sourceKey FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  N'20260925-NormalizeCommerceTransferCode'
FROM dbo.WorkSessionMovements movement
JOIN dbo.WorkSessions session ON session.WorkSessionId=movement.WorkSessionId
WHERE movement.PaymentMethodCode=N'BankTransfer';

UPDATE dbo.CustomerPaymentTenders SET MethodCode=N'Transfer'
WHERE MethodCode=N'BankTransfer';
UPDATE dbo.SupplierPaymentTenders SET MethodCode=N'Transfer'
WHERE MethodCode=N'BankTransfer';
UPDATE dbo.WorkSessionMovements SET PaymentMethodCode=N'Transfer'
WHERE PaymentMethodCode=N'BankTransfer';
DELETE worksessions.CashClosurePaymentMethodMappings
WHERE PaymentMethodCode=N'BankTransfer';
UPDATE mapping SET SourceCode=N'Transfer'
FROM dbo.AccountingSourceCategoryMappings mapping
WHERE mapping.SourceType IN(N'SupplierPaymentMethod',N'CustomerPaymentMethod')
  AND mapping.SourceCode=N'BankTransfer'
  AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceCategoryMappings currentMapping
    WHERE currentMapping.ProfileCode=mapping.ProfileCode
      AND currentMapping.SourceType=mapping.SourceType
      AND currentMapping.SourceCode=N'Transfer');
DELETE dbo.AccountingSourceCategoryMappings
WHERE SourceType IN(N'SupplierPaymentMethod',N'CustomerPaymentMethod')
  AND SourceCode=N'BankTransfer';

IF EXISTS(SELECT 1 FROM dbo.CustomerPaymentTenders WHERE MethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentTenders WHERE MethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.WorkSessionMovements WHERE PaymentMethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals WHERE PaymentMethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM worksessions.CashClosurePaymentMethodMappings WHERE PaymentMethodCode=N'BankTransfer')
 OR EXISTS(SELECT 1 FROM dbo.AccountingSourceCategoryMappings
   WHERE SourceType IN(N'SupplierPaymentMethod',N'CustomerPaymentMethod') AND SourceCode=N'BankTransfer')
  THROW 51006,N'Quedaron medios de pago antiguos después de la migración.',1;

COMMIT TRANSACTION;
