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
          CASE WHEN payment.HasCreditDocument=1 THEN source.CreditAmountSnapshot-
            CASE WHEN linked.OriginalDocumentId IS NOT NULL THEN linked.CreditReversedAmount
              WHEN delivery.ReturnTotal>source.CreditAmountSnapshot THEN source.CreditAmountSnapshot ELSE COALESCE(delivery.ReturnTotal,0) END
            -payment.AdvanceTotal ELSE 0 END RemainingCreditTotal,
          CASE WHEN sale.CollectionMode=N'Session' AND delivery.DeliveryStatus IN(N'Delivered',N'PartiallyDelivered')
            THEN CASE WHEN previouslyPaid.Amount>COALESCE(payment.InvoicePayments,0)
              THEN previouslyPaid.Amount-COALESCE(payment.InvoicePayments,0) ELSE 0 END ELSE 0 END PreviouslyCollectedTotal
        FROM dbo.DispatchSourceDocuments source
        JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
        LEFT JOIN payments payment ON payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId
        LEFT JOIN linkedReturns linked ON linked.OriginalDocumentId=source.SourceDocumentId
        LEFT JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
        CROSS APPLY(SELECT CASE WHEN linked.OriginalDocumentId IS NOT NULL THEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot-linked.RefundedAmount
          WHEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot<source.DocumentTotalSnapshot-COALESCE(delivery.ReturnTotal,0)
          THEN source.DocumentTotalSnapshot-source.CreditAmountSnapshot ELSE source.DocumentTotalSnapshot-COALESCE(delivery.ReturnTotal,0) END Amount) previouslyPaid
        WHERE source.DispatchId=@DispatchId
    )
    SELECT COALESCE(SUM(GrossCash),0) GrossCash,COALESCE(SUM(TransferTotal),0) TransferTotal,
      COALESCE(SUM(CardTotal),0) CardTotal,COALESCE(SUM(AdvanceTotal),0) AdvanceTotal,
      COALESCE(SUM(ReturnTotal),0) ReturnTotal,COALESCE(SUM(UndeliveredTotal),0) UndeliveredTotal,
      COALESCE(SUM(RemainingCreditTotal),0) RemainingCreditTotal,
      COALESCE(SUM(DocumentTotalSnapshot),0) DispatchTotal,
      COALESCE(SUM(PreviouslyCollectedTotal),0) PreviouslyCollectedTotal
    FROM documents
);
GO
