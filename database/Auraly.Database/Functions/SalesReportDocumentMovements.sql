CREATE FUNCTION reporting.SalesReportDocumentMovements(
    @TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@From DATE,@To DATE)
RETURNS TABLE
AS RETURN
(
    WITH returnTaxes AS
    (
      SELECT SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,BusinessLocalDate,SUM(TotalAmount) ExactTotal
      FROM reporting.SalesReportTaxFacts
      WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
        AND SourceDocumentType=N'SalesReturn'
      GROUP BY SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,BusinessLocalDate
    ), returnAmounts AS
    (
      -- The canonical return payment fact freezes the signed official total for
      -- refunds and customer credits alike. This is document money, never a line allocation.
      SELECT SourceDocumentId,SourceDocumentType,SUM(Amount) OfficialTotal
      FROM reporting.SalesReportPaymentFacts
      WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
        AND SourceDocumentType=N'SalesReturn'
      GROUP BY SourceDocumentId,SourceDocumentType
    )
    SELECT d.DocumentId SourceDocumentId,d.DocumentType SourceDocumentType,d.DocumentId OriginalSaleDocumentId,
      d.RoundingAdjustmentAmount,d.TotalAmount OfficialTotal,d.RestaurantTipAmount,d.BusinessLocalDate
    FROM reporting.SalesReportDocuments d
    WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId AND d.BusinessLocalDate BETWEEN @From AND @To
    UNION ALL
    SELECT t.SourceDocumentId,t.SourceDocumentType,t.OriginalSaleDocumentId,
      CONVERT(decimal(19,4),p.OfficialTotal)-CONVERT(decimal(19,4),t.ExactTotal),p.OfficialTotal,CONVERT(decimal(19,4),0),t.BusinessLocalDate
    FROM returnTaxes t JOIN returnAmounts p ON p.SourceDocumentId=t.SourceDocumentId AND p.SourceDocumentType=t.SourceDocumentType
);
