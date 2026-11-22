SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'reporting.SalesReportTaxFacts',N'U') IS NULL RETURN;

BEGIN TRANSACTION;

-- This derived link was added with the native reporting schema. Older
-- installed versions lack it; restore it from the same confirmed return.
IF COL_LENGTH(N'reporting.SalesReportTaxFacts',N'OriginalSaleDocumentId') IS NULL
    EXEC(N'ALTER TABLE reporting.SalesReportTaxFacts ADD OriginalSaleDocumentId uniqueidentifier NULL;');
IF COL_LENGTH(N'reporting.SalesReportTaxFacts',N'OfficialDocumentTotalAmount') IS NULL
    EXEC(N'ALTER TABLE reporting.SalesReportTaxFacts ADD OfficialDocumentTotalAmount decimal(19,4) NULL;');

-- Populate only the missing derived projection from the confirmed return.
-- Refunds/debt reversals are separate financial facts and may be absent or
-- smaller. No commercial, financial, inventory or fiscal engine is replayed.
EXEC sys.sp_executesql N'
    IF EXISTS(
      SELECT 1 FROM reporting.SalesReportTaxFacts fact
      LEFT JOIN dbo.Businesses business ON business.BusinessId=fact.BusinessId
      LEFT JOIN dbo.SalesReturns returned ON returned.ReturnId=fact.SourceDocumentId
        AND returned.BusinessId=fact.BusinessId
      WHERE fact.SourceDocumentType=N''SalesReturn''
        AND (returned.ReturnId IS NULL OR business.TenantId<>fact.TenantId
          OR (fact.OriginalSaleDocumentId IS NOT NULL AND fact.OriginalSaleDocumentId<>returned.OriginalDocumentId)))
      THROW 51624,N''Hay devoluciones proyectadas sin un documento original verificable en su empresa y sede.'',1;

    UPDATE fact SET OfficialDocumentTotalAmount=COALESCE(fact.OfficialDocumentTotalAmount,-returned.TotalAmount),
      OriginalSaleDocumentId=COALESCE(fact.OriginalSaleDocumentId,returned.OriginalDocumentId)
    FROM reporting.SalesReportTaxFacts fact
    JOIN dbo.SalesReturns returned ON returned.ReturnId=fact.SourceDocumentId
      AND returned.BusinessId=fact.BusinessId
    JOIN dbo.Businesses business ON business.BusinessId=fact.BusinessId AND business.TenantId=fact.TenantId
    WHERE fact.SourceDocumentType=N''SalesReturn''
      AND (fact.OfficialDocumentTotalAmount IS NULL OR fact.OriginalSaleDocumentId IS NULL);

    IF EXISTS(
      SELECT 1 FROM reporting.SalesReportTaxFacts fact
      JOIN dbo.SalesReturns returned ON returned.ReturnId=fact.SourceDocumentId AND returned.BusinessId=fact.BusinessId
      WHERE fact.SourceDocumentType=N''SalesReturn''
        AND (fact.OfficialDocumentTotalAmount IS NULL OR fact.OfficialDocumentTotalAmount<>-returned.TotalAmount
          OR fact.OriginalSaleDocumentId IS NULL OR fact.OriginalSaleDocumentId<>returned.OriginalDocumentId))
      THROW 51625,N''El total oficial proyectado de una devolución no coincide con su documento confirmado.'',1;
';

COMMIT TRANSACTION;
GO
