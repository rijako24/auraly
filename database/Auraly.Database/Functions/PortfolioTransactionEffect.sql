-- One read projection shared by portfolio detail and historical reports.
-- Settlement amounts are positive; adjustments already carry their sign.
CREATE FUNCTION accounting.PortfolioTransactionEffect(@Type nvarchar(24),@Amount decimal(19,4))
RETURNS TABLE
AS RETURN
(
    SELECT CASE WHEN @Type IN(N'Opening',N'Adjustment',N'Unapplication',N'CreditUnapplication') THEN @Amount
      WHEN @Type IN(N'Payment',N'Credit',N'Reversal',N'CreditApplication') THEN -@Amount
      END BalanceChange
);
GO
