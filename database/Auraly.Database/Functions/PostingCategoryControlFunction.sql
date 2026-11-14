-- Technical meaning of native accounting purposes. This is not
-- a PUC-code convention or a second account mapping. Both readiness and manual
-- capture consume the same rule; a configured account cannot contradict it.
CREATE FUNCTION accounting.PostingCategoryControlFunction(@Category nvarchar(128))
RETURNS TABLE
AS RETURN
(
    -- Ordinary tax, stock, revenue and expense purposes do not execute a
    -- portfolio/treasury function. Explicit control functions belong only to
    -- their declared native purposes. NULL enumerates the controls for the
    -- existing manual capture caller; it does not classify an account.
    SELECT COALESCE(@Category,meaning.Category) Category,
      COALESCE(meaning.FinancialFunction,N'General') FinancialFunction
    FROM (VALUES(0)) anchor(Value)
    LEFT JOIN (VALUES (N'AccountsReceivable',N'Receivable'),(N'AccountsPayable',N'Payable'),
      (N'Cash',N'Cash'),(N'Bank',N'Bank'),
      (N'DebitCardClearing',N'CardClearing'),(N'CreditCardClearing',N'CardClearing'),
      (N'CardClearing',N'CardClearing'),
      (N'SupplierDebitCard',N'Bank'),(N'SupplierCreditCard',N'CreditCardLiability'),
      (N'SupplierCreditsReceivable',N'SupplierAdvance'),(N'CustomerCreditsPayable',N'CustomerAdvance'),
      (N'NetPayrollPayable',N'Payable'),(N'EmployeeLoansReceivable',N'Receivable'),
      (N'EmployeeShortagesReceivable',N'Receivable'),
      -- A native inventory source remains capitalized even when its auxiliary
      -- also accepts manual unallocated costs. Eligibility for capitalization
      -- belongs to the original source/line, not this account flag alone.
      (N'Inventory',N'General'),(N'Inventory',N'UnallocatedPurchaseCost'))
      meaning(Category,FinancialFunction)
      ON @Category IS NULL OR meaning.Category=@Category
);
GO
