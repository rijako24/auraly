-- Read-only payment application facts. A payment keeps its gross cash history;
-- unapplying part of it removes only that amount from the document's paid total.
-- Consumers filter/page their positions before joining and aggregate in the
-- same command. No mutable paid total or second balance is stored here.
CREATE FUNCTION accounting.PortfolioAppliedAmounts(@BusinessId uniqueidentifier,@Kind nvarchar(16))
RETURNS TABLE
AS RETURN
(
    SELECT application.PayableId PositionId,application.Amount,payment.PaidAt OccurredAt,application.AppliedAt,
      payment.PaymentId,CAST(NULL AS uniqueidentifier) UnapplicationTransactionId
    FROM dbo.SupplierPaymentApplications application
    JOIN dbo.SupplierPayments payment ON payment.PaymentId=application.PaymentId AND payment.BusinessId=@BusinessId
    WHERE @Kind=N'Payable' AND application.AppliedAt IS NOT NULL
    UNION ALL
    SELECT reversal.PayableId,-reversal.Amount,reversal.OccurredAt,reversal.CreatedAt,
      original.SourceDocumentId,reversal.PayableTransactionId
    FROM dbo.PayableTransactions reversal
    JOIN dbo.Payables position ON position.PayableId=reversal.PayableId AND position.BusinessId=@BusinessId
    JOIN dbo.PayableTransactions original ON original.PayableTransactionId=reversal.ReversesTransactionId
      AND original.PayableId=reversal.PayableId AND original.TransactionType=N'Payment'
    WHERE @Kind=N'Payable' AND reversal.TransactionType=N'Unapplication'
    UNION ALL
    SELECT application.ReceivableId,application.Amount,payment.PaidAt,application.AppliedAt,payment.PaymentId,CAST(NULL AS uniqueidentifier)
    FROM dbo.CustomerPaymentApplications application
    JOIN dbo.CustomerPayments payment ON payment.PaymentId=application.PaymentId AND payment.BusinessId=@BusinessId
    WHERE @Kind=N'Receivable' AND application.AppliedAt IS NOT NULL
    UNION ALL
    SELECT reversal.ReceivableId,-reversal.Amount,reversal.OccurredAt,reversal.CreatedAt,
      original.SourceDocumentId,reversal.ReceivableTransactionId
    FROM dbo.ReceivableTransactions reversal
    JOIN dbo.Receivables position ON position.ReceivableId=reversal.ReceivableId AND position.BusinessId=@BusinessId
    JOIN dbo.ReceivableTransactions original ON original.ReceivableTransactionId=reversal.ReversesTransactionId
      AND original.ReceivableId=reversal.ReceivableId AND original.TransactionType=N'Payment'
    WHERE @Kind=N'Receivable' AND reversal.TransactionType=N'Unapplication'
);
GO
