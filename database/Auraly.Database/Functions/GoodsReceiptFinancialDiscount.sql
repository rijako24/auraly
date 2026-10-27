-- Read-only calculation from the immutable receipt and processed returns.
-- Callers restrict receipt identities before invoking this inline function.
CREATE FUNCTION accounting.GoodsReceiptFinancialDiscount
(
    @BusinessId uniqueidentifier,
    @GoodsReceiptId uniqueidentifier
)
RETURNS TABLE
AS RETURN
(
    SELECT CONVERT(bit,MAX(CONVERT(int,line.Anticipated))) Anticipated,
      CONVERT(decimal(19,4),SUM(line.EligibleLineAmount)) EligibleAmount
    FROM accounting.GoodsReceiptFinancialDiscountLines(@BusinessId,@GoodsReceiptId) line
    HAVING COUNT(*)>0
);
GO
