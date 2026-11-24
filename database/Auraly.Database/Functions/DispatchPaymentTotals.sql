CREATE FUNCTION dispatch.PaymentTotals(@DispatchId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    WITH payments AS
    (
        SELECT DispatchSourceDocumentId,
          SUM(CASE WHEN MethodCode=N'Cash' THEN Amount ELSE 0 END) GrossCash,
          SUM(CASE WHEN MethodCode=N'Transfer' THEN Amount ELSE 0 END) TransferTotal,
          SUM(CASE WHEN MethodCode IN(N'DebitCard',N'CreditCard') THEN Amount ELSE 0 END) CardTotal,
          SUM(CASE WHEN ApplicationType=N'CreditAdvance' THEN Amount ELSE 0 END) AdvanceTotal,
          SUM(CASE WHEN ApplicationType=N'InvoicePayment' THEN Amount ELSE 0 END) InvoicePayments,
          MAX(CASE WHEN ApplicationType=N'CreditDocument' THEN 1 ELSE 0 END) HasCreditDocument
          ,MAX(CASE WHEN ApplicationType=N'KeepCurrent' THEN 1 ELSE 0 END) HasKeptCurrent
        FROM dispatch.EffectivePayments(@DispatchId)
        GROUP BY DispatchSourceDocumentId
    ), linkedReturns AS
    (
        SELECT returned.OriginalDocumentId,
          SUM(CASE WHEN returned.EconomicResolution=N'Refund' THEN returned.TotalAmount ELSE 0 END) RefundedAmount,
          SUM(CASE WHEN returned.EconomicResolution=N'CustomerCredit' THEN returned.TotalAmount ELSE 0 END) CreditReversedAmount
        FROM dbo.DispatchSettlementOperations operation
        CROSS APPLY OPENJSON(operation.ReturnIdsJson) WITH(ReturnId uniqueidentifier '$') selected
        JOIN dbo.SalesReturns returned ON returned.ReturnId=selected.ReturnId AND returned.BusinessId=operation.BusinessId
        WHERE operation.DispatchId=@DispatchId
        GROUP BY returned.OriginalDocumentId
    ), documents AS
    (
        SELECT source.DocumentTotalSnapshot,
          COALESCE(payment.GrossCash,0) GrossCash,COALESCE(payment.TransferTotal,0) TransferTotal,
          COALESCE(payment.CardTotal,0) CardTotal,COALESCE(payment.AdvanceTotal,0) AdvanceTotal,
          COALESCE(delivery.ReturnTotal,0) ReturnTotal,
          CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN source.DocumentTotalSnapshot ELSE 0 END UndeliveredTotal,
          CASE WHEN payment.HasCreditDocument=1 THEN credit.RemainingAfterReturn-payment.AdvanceTotal ELSE 0 END RemainingCreditTotal,
          CASE WHEN (sale.CollectionMode=N'Session' OR payment.HasKeptCurrent=1) AND delivery.DeliveryStatus IN(N'Delivered',N'PartiallyDelivered')
            THEN CASE WHEN previouslyPaid.Amount>COALESCE(payment.InvoicePayments,0)
              THEN previouslyPaid.Amount-COALESCE(payment.InvoicePayments,0) ELSE 0 END ELSE 0 END
            + CASE WHEN payment.HasCreditDocument=1 THEN credit.PreviouslyCollected ELSE 0 END PreviouslyCollectedTotal
        FROM dbo.DispatchSourceDocuments source
        JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
        LEFT JOIN dbo.Receivables receivable ON receivable.BusinessId=sale.BusinessId
          AND receivable.SourceDocumentId=sale.DocumentId AND receivable.SourceDocumentType=sale.DocumentType
          AND receivable.SourceLineId IS NULL
        LEFT JOIN payments payment ON payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId
        LEFT JOIN linkedReturns linked ON linked.OriginalDocumentId=source.SourceDocumentId
        LEFT JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
        CROSS APPLY(SELECT
          CASE WHEN receivable.OutstandingAmount>COALESCE(delivery.ReturnTotal,0)
            THEN receivable.OutstandingAmount-COALESCE(delivery.ReturnTotal,0) ELSE 0 END RemainingAfterReturn,
          CASE WHEN source.CreditAmountSnapshot>receivable.OutstandingAmount THEN
            CASE WHEN COALESCE(delivery.ReturnTotal,0)>receivable.OutstandingAmount THEN
              CASE WHEN source.CreditAmountSnapshot>COALESCE(delivery.ReturnTotal,0)
                THEN source.CreditAmountSnapshot-COALESCE(delivery.ReturnTotal,0) ELSE 0 END
              ELSE source.CreditAmountSnapshot-receivable.OutstandingAmount END ELSE 0 END PreviouslyCollected) credit
        CROSS APPLY(SELECT CASE WHEN linked.OriginalDocumentId IS NOT NULL THEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot-linked.RefundedAmount
          WHEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot<source.DocumentTotalSnapshot-COALESCE(delivery.ReturnTotal,0)
          THEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot ELSE source.DocumentTotalSnapshot-COALESCE(delivery.ReturnTotal,0) END Amount) previouslyPaid
        WHERE source.DispatchId=@DispatchId
    )
    , totals AS
    (SELECT COALESCE(SUM(GrossCash),0) GrossCash,COALESCE(SUM(TransferTotal),0) TransferTotal,
      COALESCE(SUM(CardTotal),0) CardTotal,COALESCE(SUM(AdvanceTotal),0) AdvanceTotal,
      COALESCE(SUM(ReturnTotal),0) ReturnTotal,COALESCE(SUM(UndeliveredTotal),0) UndeliveredTotal,
      COALESCE(SUM(RemainingCreditTotal),0) RemainingCreditTotal,
      COALESCE(SUM(DocumentTotalSnapshot),0) DispatchTotal,
      COALESCE(SUM(PreviouslyCollectedTotal),0) PreviouslyCollectedTotal
    FROM documents)
    SELECT totals.GrossCash,totals.TransferTotal,totals.CardTotal,totals.AdvanceTotal,
      totals.ReturnTotal,totals.UndeliveredTotal,
      COALESCE(closure.DispatchCreditDocumentTotal,totals.RemainingCreditTotal) RemainingCreditTotal,
      totals.DispatchTotal,
      COALESCE(TRY_CONVERT(decimal(19,4),JSON_VALUE(closure.ReceiptSnapshotJson,N'$.previouslyCollectedTotal')),
        totals.PreviouslyCollectedTotal) PreviouslyCollectedTotal
    FROM totals
    LEFT JOIN dbo.WorkSessions session ON session.DispatchId=@DispatchId AND session.Kind=N'Dispatch'
    LEFT JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
);
GO
