-- EntryId is selected before expanding lines. The journal is immutable; only
-- accepted Purchasing assignments consume its cost budget, never payments.
CREATE FUNCTION purchasing.ReceiptCostVoucherLines(@EntryId uniqueidentifier)
RETURNS TABLE
AS RETURN
    SELECT line.EntryId,line.LineNumber,line.AccountId,account.Code AccountCode,
      account.Name AccountName,line.PartyId,line.CostCenterId,line.Description,
      line.Debit,line.Credit,
      CONVERT(decimal(19,4),CASE WHEN line.Debit>0
        AND (account.RequiresParty=0 OR line.PartyId IS NOT NULL) AND
        ((account.AccountType=N'Expense' AND COALESCE(account.FinancialFunction,N'General')=N'General')
          OR (account.AccountType=N'Asset' AND account.FinancialFunction=N'UnallocatedPurchaseCost'
            AND entry.SourceDocumentType=N'ManualAccountingVoucher'))
        AND entry.SourceDocumentType IN(N'Expense',N'ManualAccountingVoucher',N'GoodsReceiptCostDocument')
        AND NOT EXISTS(SELECT 1 FROM dbo.Expenses expense WHERE expense.ExpenseId=entry.SourceDocumentId
          AND entry.SourceDocumentType=N'Expense' AND expense.Status<>N'Processed')
        AND NOT EXISTS(SELECT 1 FROM dbo.SalesReturnCharges returned WHERE returned.AppliedChargeId=entry.SourceDocumentId
          AND entry.SourceDocumentType=N'Expense')
        AND NOT EXISTS(SELECT 1 FROM dbo.Payables payable
          JOIN accounting.SourcePositionIndex cancellation ON cancellation.BusinessId=payable.BusinessId
            AND cancellation.SubledgerKind=N'Payable' AND cancellation.SubledgerId=payable.PayableId
            AND cancellation.EffectKind=N'Cancellation'
          WHERE entry.SourceDocumentType=N'ManualAccountingVoucher'
            AND payable.SourceDocumentId=entry.SourceDocumentId AND payable.SourceDocumentType=entry.SourceDocumentType
            AND payable.BusinessId=entry.BusinessId)
        THEN line.Debit ELSE 0 END) EligibleFunctionalAmount,
      COALESCE(used.FunctionalAmount,0) AssignedFunctionalAmount
    FROM dbo.AccountingEntries entry
    JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
    JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=entry.TenantId
    OUTER APPLY(SELECT SUM(cost.FunctionalAmount) FunctionalAmount
      FROM purchasing.GoodsReceiptCostLines cost
      WHERE cost.SourceEntryId=line.EntryId AND cost.SourceEntryLineNumber=line.LineNumber) used
    WHERE entry.EntryId=@EntryId;
GO
