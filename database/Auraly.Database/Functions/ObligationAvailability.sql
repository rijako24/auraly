CREATE FUNCTION accounting.ObligationAvailability
(
    @BusinessId uniqueidentifier,
    @SubledgerKind nvarchar(16),
    @SubledgerId uniqueidentifier,
    @OutstandingAmount decimal(19,4),
    @ExcludedAdjustmentId uniqueidentifier
)
RETURNS TABLE
AS RETURN
(
    SELECT reserved.PendingPaymentAmount,adjustments.PendingAdjustmentAmount,
        @OutstandingAmount-reserved.PendingPaymentAmount-adjustments.PendingAdjustmentAmount-applications.PendingApplicationAmount AvailableAmount,
        adjustments.PendingAdjustmentCount,applications.PendingApplicationAmount,applications.PendingCancellationCount,
        applications.SourceCancelled
    FROM (SELECT
        COALESCE((SELECT SUM(payment.Amount) FROM
        (
            SELECT a.Amount+COALESCE(a.FinancialDiscountAdjustment,0) Amount
            FROM dbo.SupplierPaymentApplications a
            JOIN dbo.SupplierPayments p ON p.PaymentId=a.PaymentId
            WHERE @SubledgerKind=N'Payable' AND a.PayableId=@SubledgerId
                AND p.BusinessId=@BusinessId AND a.AppliedAt IS NULL AND p.Status=N'Accepted'
            UNION ALL
            SELECT a.Amount
            FROM dbo.CustomerPaymentApplications a
            JOIN dbo.CustomerPayments p ON p.PaymentId=a.PaymentId
            WHERE @SubledgerKind=N'Receivable' AND a.ReceivableId=@SubledgerId
                AND p.BusinessId=@BusinessId AND a.AppliedAt IS NULL AND p.Status=N'Accepted'
        ) payment),CONVERT(decimal(19,4),0)) PendingPaymentAmount) reserved
    CROSS APPLY (SELECT
        COALESCE(SUM(CASE WHEN JSON_VALUE(s.PayloadJson,'$.Direction')=N'Decrease'
            THEN CONVERT(decimal(19,4),JSON_VALUE(s.PayloadJson,'$.Amount')) ELSE 0 END),
            CONVERT(decimal(19,4),0)) PendingAdjustmentAmount,
        COUNT_BIG(*) PendingAdjustmentCount
            FROM dbo.AccountingSourceDocuments s
            WHERE s.BusinessId=@BusinessId AND s.SourceDocumentType=N'AccountAdjustment'
                AND s.AdjustmentObligationId=@SubledgerId
                AND (@ExcludedAdjustmentId IS NULL OR s.SourceDocumentId<>@ExcludedAdjustmentId)
                AND JSON_VALUE(s.PayloadJson,'$.SubledgerKind')=@SubledgerKind
                AND NOT EXISTS(SELECT 1 FROM dbo.PayableTransactions t
                    WHERE @SubledgerKind=N'Payable' AND t.PayableId=@SubledgerId
                        AND t.TransactionType=N'Adjustment' AND t.SourceDocumentId=s.SourceDocumentId)
                AND NOT EXISTS(SELECT 1 FROM dbo.ReceivableTransactions t
                    WHERE @SubledgerKind=N'Receivable' AND t.ReceivableId=@SubledgerId
                        AND t.TransactionType=N'Adjustment' AND t.SourceDocumentId=s.SourceDocumentId)
    ) adjustments
    CROSS APPLY(SELECT COALESCE(SUM(target.Amount),CONVERT(decimal(19,4),0)) PendingApplicationAmount,
        COALESCE(MAX(CASE WHEN target.EffectKind=N'Cancellation' THEN 1 ELSE 0 END),0) PendingCancellationCount,
        CONVERT(bit,COALESCE(MAX(CASE WHEN target.EffectKind=N'Cancelled' THEN 1 ELSE 0 END),0)) SourceCancelled
      FROM (
      SELECT target.Amount,target.EffectKind FROM accounting.SourcePositionIndex target
      WHERE target.BusinessId=@BusinessId AND target.SubledgerKind=@SubledgerKind AND target.SubledgerId=@SubledgerId
        AND target.EffectKind IN(N'CreditApplication',N'Cancellation')
        AND NOT EXISTS(SELECT 1 FROM dbo.PayableTransactions applied WHERE @SubledgerKind=N'Payable'
          AND applied.PayableTransactionId=target.EffectId AND applied.PayableId=@SubledgerId
          AND applied.SourceDocumentId=target.SourceDocumentId AND applied.SourceDocumentType=target.SourceDocumentType
          AND applied.TransactionType=CASE WHEN target.EffectKind=N'Cancellation' THEN N'Credit' ELSE N'CreditApplication' END)
        AND NOT EXISTS(SELECT 1 FROM dbo.ReceivableTransactions applied WHERE @SubledgerKind=N'Receivable'
          AND applied.ReceivableTransactionId=target.EffectId AND applied.ReceivableId=@SubledgerId
          AND applied.SourceDocumentId=target.SourceDocumentId AND applied.SourceDocumentType=target.SourceDocumentType
          AND applied.TransactionType=N'CreditApplication')
        AND NOT EXISTS(SELECT 1 FROM dbo.SupplierCreditTransactions applied WHERE @SubledgerKind=N'SupplierCredit'
          AND applied.SupplierCreditTransactionId=target.EffectId AND applied.SupplierCreditId=@SubledgerId
          AND applied.SourceDocumentId=target.SourceDocumentId AND applied.SourceDocumentType=target.SourceDocumentType
          AND applied.TransactionType=N'Application')
        AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCreditTransactions applied WHERE @SubledgerKind=N'CustomerCredit'
          AND applied.CustomerCreditTransactionId=target.EffectId AND applied.CustomerCreditId=@SubledgerId
          AND applied.SourceDocumentId=target.SourceDocumentId AND applied.SourceDocumentType=target.SourceDocumentType
          AND applied.TransactionType=N'Application')
      UNION ALL
      -- Native expense cancellation already owns its durable state. Reserve
      -- its entire outstanding obligation here, alongside manual cancellation,
      -- until the same accounting processor applies it. No second balance/index.
      SELECT CASE WHEN expense.Status=N'CancellationPending' THEN @OutstandingAmount ELSE 0 END,
        CASE WHEN expense.Status=N'CancellationPending' THEN N'Cancellation' ELSE N'Cancelled' END
      FROM dbo.Payables payable
      JOIN dbo.Expenses expense ON expense.ExpenseId=payable.SourceDocumentId
        AND expense.BusinessId=payable.BusinessId AND expense.Status IN(N'CancellationPending',N'Cancelled')
      WHERE @SubledgerKind=N'Payable' AND payable.PayableId=@SubledgerId
        AND payable.BusinessId=@BusinessId AND payable.SourceDocumentType=N'Expense'
      ) target
    ) applications
);
GO
