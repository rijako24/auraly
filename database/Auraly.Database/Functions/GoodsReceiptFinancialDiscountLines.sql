-- Frozen eligible amount per purchased line after processed returns.
CREATE FUNCTION accounting.GoodsReceiptFinancialDiscountLines
(
    @BusinessId uniqueidentifier,
    @GoodsReceiptId uniqueidentifier
)
RETURNS TABLE
AS RETURN
(
    SELECT discount.Anticipated,discountLine.ReceiptLineNumber,
      CONVERT(decimal(19,4),discountLine.Amount-COALESCE(returned.Amount,0)) EligibleLineAmount,
      CONVERT(decimal(19,6),originalLine.Quantity-COALESCE(returned.Quantity,0)) EligibleQuantity
    FROM dbo.AccountingSourceDocuments source
    CROSS APPLY OPENJSON(source.PayloadJson,'$.financialDiscount')
      WITH(Anticipated bit '$.anticipatedInCost',Lines nvarchar(max) '$.lines' AS JSON) discount
    CROSS APPLY OPENJSON(discount.Lines)
      WITH(ReceiptLineNumber int '$.receiptLineNumber',Amount decimal(19,4) '$.amount') discountLine
    JOIN dbo.GoodsReceiptLines originalLine ON originalLine.GoodsReceiptId=@GoodsReceiptId
      AND originalLine.LineNumber=discountLine.ReceiptLineNumber
    OUTER APPLY(
      SELECT CASE WHEN discount.Anticipated=1 THEN SUM(returnedLine.FinancialDiscountAmount)
        ELSE ROUND(discountLine.Amount*SUM(returnedLine.Quantity)/originalLine.Quantity,4) END Amount,
        SUM(returnedLine.Quantity) Quantity
      FROM dbo.PurchaseReturnLines returnedLine
      JOIN dbo.PurchaseReturns purchaseReturn ON purchaseReturn.PurchaseReturnId=returnedLine.PurchaseReturnId
        AND purchaseReturn.BusinessId=@BusinessId AND purchaseReturn.Status=N'Processed'
      WHERE returnedLine.OriginalGoodsReceiptId=@GoodsReceiptId
        AND returnedLine.OriginalLineNumber=discountLine.ReceiptLineNumber) returned
    WHERE source.BusinessId=@BusinessId AND source.SourceDocumentId=@GoodsReceiptId
      AND source.SourceDocumentType=N'GoodsReceipt'
);
GO
