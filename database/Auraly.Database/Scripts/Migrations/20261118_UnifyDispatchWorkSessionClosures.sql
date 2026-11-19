/* Versioned schema/data cutover. Run while API and settlement consumers are
   quiescent, before DacFx computes its deployment plan. No financial replay. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.DispatchSourceDocuments',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.DispatchSourceDocuments',N'CreditAmountSnapshot') IS NULL
BEGIN
  BEGIN TRANSACTION;
  BEGIN TRY
    ALTER TABLE dbo.DispatchSourceDocuments ADD CreditAmountSnapshot DECIMAL(19,4) NULL;
    -- Existing manifests retain the original credit they used. Do not net or
    -- reinterpret completed/historical deliveries while introducing the field.
    EXEC sys.sp_executesql N'UPDATE source SET CreditAmountSnapshot=sale.CreditAmount
      FROM dbo.DispatchSourceDocuments source JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId;
      ALTER TABLE dbo.DispatchSourceDocuments ALTER COLUMN CreditAmountSnapshot DECIMAL(19,4) NOT NULL;';
    COMMIT;
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
  END CATCH;
END;
IF OBJECT_ID(N'dbo.DispatchSettlements',N'U') IS NOT NULL
BEGIN
  IF EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations operation JOIN dbo.DispatchSettlements legacy ON legacy.DispatchId=operation.DispatchId WHERE operation.Status<>N'Completed')
    THROW 51618,N'Completa o revisa las liquidaciones pendientes antes de unificar los cierres.',1;
  BEGIN TRANSACTION;
  BEGIN TRY
    -- Preserve the amount displayed by the previous delivery flow. Native events
    -- capture the canonical return valuation at acceptance; runtime has no legacy
    -- formula or account/date compatibility branch.
    IF OBJECT_ID(N'dbo.DispatchDeliveryEvents',N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.DispatchDeliveryEvents',N'ReturnTotal') IS NULL
    BEGIN
        ALTER TABLE dbo.DispatchDeliveryEvents ADD ReturnTotal DECIMAL(19,4) NOT NULL
          CONSTRAINT DF_DispatchDeliveryEvents_ReturnTotal DEFAULT(0);
        EXEC sys.sp_executesql N'
          UPDATE delivery SET ReturnTotal=COALESCE(returned.Amount,0)
          FROM dbo.DispatchDeliveryEvents delivery
          OUTER APPLY(SELECT SUM(ROUND(line.LineTotal/NULLIF(line.Quantity,0)*item.Quantity,2)) Amount
            FROM dbo.DispatchDeliveryReturns item
            JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=item.DispatchSourceDocumentId
            JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId AND line.LineNumber=item.OriginalLineNumber
            WHERE item.DispatchSourceDocumentId=delivery.DispatchSourceDocumentId) returned;';
    END;
    IF COL_LENGTH(N'dbo.WorkSessions',N'Kind') IS NULL
      ALTER TABLE dbo.WorkSessions ADD Kind NVARCHAR(16) NOT NULL CONSTRAINT DF_WorkSessions_Kind DEFAULT(N'Normal');
    IF COL_LENGTH(N'dbo.WorkSessions',N'DispatchId') IS NULL
      ALTER TABLE dbo.WorkSessions ADD DispatchId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'dbo.WorkSessionClosures',N'DispatchStatus') IS NULL
      ALTER TABLE dbo.WorkSessionClosures ADD DispatchStatus NVARCHAR(32) NULL,
        DispatchExpectedCash DECIMAL(19,4) NULL,DispatchTransferTotal DECIMAL(19,4) NULL,
        DispatchCreditDocumentTotal DECIMAL(19,4) NULL,DispatchCreditAdvanceTotal DECIMAL(19,4) NULL,
        DispatchReturnTotal DECIMAL(19,4) NULL,ReceivedCash DECIMAL(19,4) NULL,
        ReceivedByUserId UNIQUEIDENTIFIER NULL,ReceivedAt DATETIMEOFFSET(7) NULL,DispatchReviewNotes NVARCHAR(500) NULL;
    -- Earlier deployed headers did not capture card/transfer declarations.
    -- NULL preserves that absence; it must never be presented as a zero count.
    IF COL_LENGTH(N'dbo.DispatchSettlements',N'DeclaredCard') IS NULL
      ALTER TABLE dbo.DispatchSettlements ADD DeclaredCard DECIMAL(19,4) NULL;
    IF COL_LENGTH(N'dbo.DispatchSettlements',N'DeclaredTransfer') IS NULL
      ALTER TABLE dbo.DispatchSettlements ADD DeclaredTransfer DECIMAL(19,4) NULL;
    -- New columns are compiled after the mechanical additions above. Repeating
    -- the migration keeps every closure ID and never opens a second session.
    EXEC sys.sp_executesql N'
      INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,UserId,Kind,DispatchId,OpenedAt,LastActivityAt,ClosedAt,Status)
      SELECT NEWID(),dispatch.TenantId,dispatch.BusinessId,settlement.TransporterClosedBy,N''Dispatch'',
        settlement.DispatchId,settlement.TransporterClosedAt,settlement.TransporterClosedAt,settlement.TransporterClosedAt,N''Closed''
      FROM dbo.DispatchSettlements settlement JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=settlement.DispatchId
      WHERE NOT EXISTS(SELECT 1 FROM dbo.WorkSessions session WHERE session.DispatchId=settlement.DispatchId);
      INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
        TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,CountedCash,CashDifference,
        SalesCount,CreditSalesCount,CreditSalesAmount,ReturnCount,Note,ReceiptSnapshotJson,ReceiptHash,ClosedAt,ReconciliationStatus,DispatchStatus,DispatchExpectedCash,
        DispatchTransferTotal,DispatchCreditDocumentTotal,DispatchCreditAdvanceTotal,DispatchReturnTotal,
        ReceivedCash,ReceivedByUserId,ReceivedAt,DispatchReviewNotes)
      SELECT settlement.DispatchSettlementId,session.WorkSessionId,settlement.TransporterClosedBy,settlement.IdempotencyKey,
        totals.TotalAmount,settlement.ReturnTotal,-totals.UndeliveredAmount,totals.TotalAmount-settlement.ReturnTotal-totals.UndeliveredAmount,
        settlement.ExpectedCash,settlement.DeclaredCash,settlement.DeclaredCash-settlement.ExpectedCash,
        totals.SalesCount,credits.DocumentCount,settlement.CreditDocumentTotal,returns.DocumentCount,settlement.DifferenceReason,snapshot.Payload,
        HASHBYTES(''SHA2_256'',CONVERT(varchar(max),snapshot.Payload COLLATE Latin1_General_100_CI_AS_SC_UTF8)),
        settlement.TransporterClosedAt,CASE WHEN settlement.Status=N''Completed'' THEN
          CASE WHEN settlement.CashDifference=0 THEN N''Reconciled'' ELSE N''ReconciledWithDifferences'' END ELSE N''Pending'' END,
        settlement.Status,settlement.ExpectedCash,settlement.DepositTotal,settlement.CreditDocumentTotal,
        settlement.CreditAdvanceTotal,settlement.ReturnTotal,settlement.CashReceived,settlement.ReceivedBy,
        settlement.ReceivedAt,settlement.Notes
      FROM dbo.DispatchSettlements settlement
      JOIN dbo.WorkSessions session ON session.DispatchId=settlement.DispatchId AND session.Kind=N''Dispatch''
      CROSS APPLY(SELECT COALESCE(SUM(DocumentTotalSnapshot),0) TotalAmount,COUNT_BIG(*) SalesCount,
        COALESCE(SUM(CASE WHEN Status=N''NotDelivered'' THEN DocumentTotalSnapshot ELSE 0 END),0) UndeliveredAmount
        FROM dbo.DispatchSourceDocuments WHERE DispatchId=settlement.DispatchId) totals
      CROSS APPLY(SELECT COUNT(*) DocumentCount FROM dbo.DispatchDeliveryPayments
        WHERE DispatchId=settlement.DispatchId AND ApplicationType=N''CreditDocument'') credits
      CROSS APPLY(SELECT COUNT_BIG(DISTINCT DispatchSourceDocumentId) DocumentCount FROM dbo.DispatchDeliveryReturns
        WHERE DispatchId=settlement.DispatchId) returns
      CROSS APPLY(SELECT settlement.DispatchSettlementId workSessionClosureId,session.WorkSessionId workSessionId,
        settlement.DispatchId dispatchId,settlement.TransporterClosedBy closedByUserId,settlement.TransporterClosedAt closedAt,
        settlement.DeclaredCash declaredCash,settlement.DeclaredCard declaredCard,settlement.DeclaredTransfer declaredTransfer,
        JSON_QUERY((SELECT settlement.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) legacySettlement
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) snapshot(Payload)
      WHERE NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosures closure WHERE closure.WorkSessionClosureId=settlement.DispatchSettlementId);
      INSERT dbo.WorkSessionClosurePaymentTotals(WorkSessionClosureId,PaymentMethodCode,SalesAmount,RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
      SELECT settlement.DispatchSettlementId,valueset.Method,valueset.Amount,0,0,valueset.Amount,valueset.Counted,valueset.Counted-valueset.Amount
      FROM dbo.DispatchSettlements settlement
      CROSS APPLY(VALUES(N''Cash'',settlement.ExpectedCash,settlement.DeclaredCash),
        (N''Card'',COALESCE((SELECT SUM(payment.Amount) FROM dbo.DispatchDeliveryPayments payment WHERE payment.DispatchId=settlement.DispatchId AND payment.PaymentMethod IN(N''DebitCard'',N''CreditCard'')),0),settlement.DeclaredCard),
        (N''Transfer'',settlement.DepositTotal,settlement.DeclaredTransfer),
        (N''Credit'',settlement.CreditDocumentTotal,CONVERT(decimal(19,4),NULL))) valueset(Method,Amount,Counted)
      WHERE NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals total
        WHERE total.WorkSessionClosureId=settlement.DispatchSettlementId AND total.PaymentMethodCode=valueset.Method);
      IF EXISTS(SELECT DispatchSettlementId,BusinessId,DispatchId,ExpectedCash,DeclaredCash,CashDifference,
          DepositTotal,CreditDocumentTotal,CreditAdvanceTotal,ReturnTotal,DifferenceReason,TransporterClosedBy,
          TransporterClosedAt,CashReceived,ReceivedBy,ReceivedAt,Notes,Status,IdempotencyKey,DeclaredCard,DeclaredTransfer
        FROM dbo.DispatchSettlements
        EXCEPT
        SELECT closure.WorkSessionClosureId,session.BusinessId,session.DispatchId,closure.DispatchExpectedCash,
          closure.CountedCash,COALESCE(closure.ReceivedCash,closure.CountedCash)-closure.DispatchExpectedCash,
          closure.DispatchTransferTotal,closure.DispatchCreditDocumentTotal,closure.DispatchCreditAdvanceTotal,
          closure.DispatchReturnTotal,closure.Note,closure.ClosedByUserId,closure.ClosedAt,closure.ReceivedCash,
          closure.ReceivedByUserId,closure.ReceivedAt,closure.DispatchReviewNotes,closure.DispatchStatus,closure.IdempotencyKey,
          card.CountedAmount,transfer.CountedAmount
        FROM dbo.WorkSessionClosures closure JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
        LEFT JOIN dbo.WorkSessionClosurePaymentTotals card ON card.WorkSessionClosureId=closure.WorkSessionClosureId AND card.PaymentMethodCode=N''Card''
        LEFT JOIN dbo.WorkSessionClosurePaymentTotals transfer ON transfer.WorkSessionClosureId=closure.WorkSessionClosureId AND transfer.PaymentMethodCode=N''Transfer'')
        THROW 51618,N''La migración no conserva los cierres originales.'',1;
      DROP TABLE dbo.DispatchSettlements;';
    COMMIT;
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
  END CATCH;
END;

-- ClaimGet now claims atomically. DacFx preserves objects absent from the model,
-- so retire its obsolete second writer explicitly, including on a resumed cut.
DROP PROCEDURE IF EXISTS dbo.DispatchSettlementOperationClaimMark;
