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

-- Preserve known official totals; absence of historical evidence remains NULL,
-- never zero or a guessed rounded tax total. Imported report-only facts may
-- predate operational return roots. Their previous official total, when any,
-- is the existing signed payment aggregate, copied once during this migration.
-- Runtime reads use only the same canonical projected field for every return.
EXEC sys.sp_executesql N'
    SELECT fact.SourceDocumentId,fact.BusinessId,fact.TenantId,
      returned.OriginalDocumentId,returned.BusinessId ReturnBusinessId,
      COALESCE(-returned.TotalAmount,payment.OfficialTotal) ExpectedTotal,
      CONVERT(bit,CASE WHEN returned.ReturnId IS NOT NULL THEN 1 ELSE 0 END) HasConfirmedReturn
    INTO #ReturnEvidence
    FROM (SELECT DISTINCT SourceDocumentId,BusinessId,TenantId
      FROM reporting.SalesReportTaxFacts WHERE SourceDocumentType=N''SalesReturn'') fact
    LEFT JOIN dbo.SalesReturns returned ON returned.ReturnId=fact.SourceDocumentId
    LEFT JOIN (SELECT SourceDocumentId,BusinessId,TenantId,SUM(Amount) OfficialTotal
      FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentType=N''SalesReturn''
      GROUP BY SourceDocumentId,BusinessId,TenantId) payment
      ON payment.SourceDocumentId=fact.SourceDocumentId AND payment.BusinessId=fact.BusinessId
      AND payment.TenantId=fact.TenantId;
    CREATE UNIQUE CLUSTERED INDEX IX_ReturnEvidence ON #ReturnEvidence(SourceDocumentId,BusinessId,TenantId);

    IF EXISTS(
      SELECT 1 FROM reporting.SalesReportTaxFacts fact
      LEFT JOIN dbo.Businesses business ON business.BusinessId=fact.BusinessId
      JOIN #ReturnEvidence evidence ON evidence.SourceDocumentId=fact.SourceDocumentId
        AND evidence.BusinessId=fact.BusinessId AND evidence.TenantId=fact.TenantId
      WHERE fact.SourceDocumentType=N''SalesReturn''
        AND (business.BusinessId IS NULL OR business.TenantId<>fact.TenantId
          OR (evidence.ReturnBusinessId IS NOT NULL AND evidence.ReturnBusinessId<>fact.BusinessId)
          OR (evidence.HasConfirmedReturn=1 AND fact.OriginalSaleDocumentId IS NOT NULL
            AND fact.OriginalSaleDocumentId<>evidence.OriginalDocumentId)))
      THROW 51624,N''Hay devoluciones proyectadas con una empresa, sede o referencia original inconsistente.'',1;

    UPDATE fact SET OfficialDocumentTotalAmount=COALESCE(fact.OfficialDocumentTotalAmount,evidence.ExpectedTotal),
      OriginalSaleDocumentId=COALESCE(fact.OriginalSaleDocumentId,evidence.OriginalDocumentId)
    FROM reporting.SalesReportTaxFacts fact
    JOIN #ReturnEvidence evidence ON evidence.SourceDocumentId=fact.SourceDocumentId
      AND evidence.BusinessId=fact.BusinessId AND evidence.TenantId=fact.TenantId
    WHERE fact.SourceDocumentType=N''SalesReturn''
      AND ((fact.OfficialDocumentTotalAmount IS NULL AND evidence.ExpectedTotal IS NOT NULL)
        OR (fact.OriginalSaleDocumentId IS NULL AND evidence.OriginalDocumentId IS NOT NULL));

    IF EXISTS(
      SELECT 1 FROM reporting.SalesReportTaxFacts fact
      JOIN #ReturnEvidence evidence ON evidence.SourceDocumentId=fact.SourceDocumentId
        AND evidence.BusinessId=fact.BusinessId AND evidence.TenantId=fact.TenantId
      WHERE fact.SourceDocumentType=N''SalesReturn''
        AND ((evidence.ExpectedTotal IS NOT NULL AND
              (fact.OfficialDocumentTotalAmount IS NULL OR fact.OfficialDocumentTotalAmount<>evidence.ExpectedTotal))
          OR fact.OfficialDocumentTotalAmount>=0
          OR (evidence.HasConfirmedReturn=1 AND
              (fact.OriginalSaleDocumentId IS NULL OR fact.OriginalSaleDocumentId<>evidence.OriginalDocumentId))))
      THROW 51625,N''El total oficial proyectado de una devolución no coincide con su evidencia conservada.'',1;

    DECLARE @Unknown bigint=(SELECT COUNT_BIG(*) FROM #ReturnEvidence WHERE ExpectedTotal IS NULL);
    IF @Unknown>0 PRINT CONCAT(N''Devoluciones históricas importadas sin total oficial verificable, conservadas sin inferir: '',@Unknown);
';

COMMIT TRANSACTION;
GO
