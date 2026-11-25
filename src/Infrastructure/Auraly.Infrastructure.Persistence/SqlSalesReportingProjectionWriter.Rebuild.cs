using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProjectionWriter
{
    // The rebuild owns the temporary manifest and the Reporting lock. Retained
    // rows remain evidence, not newly accepted operational or financial sources.
    internal async Task RestorePreservedHistoryAsync(SalesReportingSqlSession session,
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        var sql = """
            IF NOT EXISTS(SELECT 1 FROM #PreservedReturns) AND NOT EXISTS(SELECT 1 FROM #PreservedSales) RETURN;
            DECLARE @SourceDocumentId uniqueidentifier=NULL,@SourceDocumentType nvarchar(32)=N'SalesReturn',
              @DocumentCount bigint=0,@LocalDate date,@ThroughDate date;
            -- Upgrade only derived reader metadata after validating the retained
            -- graph. Monetary snapshots, facts, labels and hashes stay intact.
            WITH lines AS(
              SELECT f.SourceDocumentId,SUM(f.Quantity) Quantity,
                SUM(CONVERT(bigint,1-CONVERT(int,f.CostAvailable))) MissingCost
              FROM reporting.SalesReportLineFacts f JOIN #PreservedSales s ON s.SourceDocumentId=f.SourceDocumentId
                AND s.SourceDocumentType=f.SourceDocumentType
              WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId GROUP BY f.SourceDocumentId
            )
            UPDATE original SET ProductQuantity=l.Quantity,MissingCostLineCount=l.MissingCost,
              SourceDocumentAvailable=0,
              ProjectionVersion=@Version,ProjectedAt=@Now
            FROM reporting.SalesReportDocuments original JOIN lines l ON l.SourceDocumentId=original.DocumentId
            WHERE original.BusinessId=@BusinessId AND original.TenantId=@TenantId;

            SELECT @BusinessId BusinessId,d.BusinessLocalDate,d.CurrencyCode,COUNT_BIG(*) DocumentCount,
              SUM(d.ProductQuantity) UnitsSold,CONVERT(decimal(19,6),0) UnitsReturned,
              SUM(d.GrossAmount) GrossSales,SUM(d.DiscountAmount) Discounts,CONVERT(decimal(19,4),0) Returns,
              SUM(d.UntaxedAmount) NetUntaxedSales,SUM(d.TaxAmount) NetTax,SUM(d.TotalAmount) NetTotalSales,
              SUM(CASE WHEN d.MissingCostLineCount=0 THEN COALESCE(d.RecognizedCostAmount,0) ELSE 0 END) NetRecognizedCost,
              SUM(CASE WHEN d.MissingCostLineCount=0 THEN d.UntaxedAmount-COALESCE(d.RecognizedCostAmount,0) ELSE 0 END) GrossProfit,
              SUM(d.CreditAmount) CreditSales,SUM(d.CollectedAmount) Collected,CONVERT(decimal(19,4),0) Refunded,
              SUM(CONVERT(bigint,CASE WHEN d.MissingCostLineCount>0 OR d.RecognizedCostAmount IS NULL THEN 1 ELSE 0 END)) UnknownCostDocumentCount
            INTO #DailyDeltas FROM reporting.SalesReportDocuments d JOIN #PreservedSales s ON s.SourceDocumentId=d.DocumentId
            WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId GROUP BY d.BusinessLocalDate,d.CurrencyCode;

            SELECT @LocalDate=MIN(f.BusinessLocalDate),@ThroughDate=MAX(f.BusinessLocalDate)
            FROM reporting.SalesReportTaxFacts f JOIN #PreservedReturns retained ON retained.SourceDocumentId=f.SourceDocumentId
            WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId AND f.SourceDocumentType=N'SalesReturn';

            UPDATE fact SET OriginalSaleDocumentId=retained.OriginalSaleDocumentId
            FROM reporting.SalesReportTaxFacts fact JOIN #PreservedReturns retained ON retained.SourceDocumentId=fact.SourceDocumentId
            WHERE fact.BusinessId=@BusinessId AND fact.TenantId=@TenantId AND fact.SourceDocumentType=N'SalesReturn'
              AND fact.OriginalSaleDocumentId IS NULL;
            ;WITH taxes AS(
              SELECT f.SourceDocumentId,f.BusinessLocalDate,SUM(f.TaxableAmount) Base,SUM(f.TaxAmount) Tax,
                SUM(f.TotalAmount) ExactTotal,MAX(f.OfficialDocumentTotalAmount) OfficialTotal
              FROM reporting.SalesReportTaxFacts f JOIN #PreservedReturns r ON r.SourceDocumentId=f.SourceDocumentId
              WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId AND f.SourceDocumentType=N'SalesReturn'
              GROUP BY f.SourceDocumentId,f.BusinessLocalDate
            ), lines AS(
              SELECT f.SourceDocumentId,SUM(f.Quantity) Quantity,SUM(f.RecognizedCostAmount) Cost,
                MAX(CONVERT(int,1-f.CostAvailable)) MissingCost
              FROM reporting.SalesReportLineFacts f JOIN #PreservedReturns r ON r.SourceDocumentId=f.SourceDocumentId
              WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId AND f.SourceDocumentType=N'SalesReturn'
              GROUP BY f.SourceDocumentId
            ), payments AS(
              SELECT p.SourceDocumentId,-SUM(CASE WHEN p.MovementType=N'Refund' THEN p.Amount ELSE 0 END) Refunded
              FROM reporting.SalesReportPaymentFacts p JOIN #PreservedReturns r ON r.SourceDocumentId=p.SourceDocumentId
              WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.SourceDocumentType=N'SalesReturn'
              GROUP BY p.SourceDocumentId
            )
            INSERT #DailyDeltas
            SELECT @BusinessId BusinessId,t.BusinessLocalDate,N'COP' CurrencyCode,CONVERT(bigint,0) DocumentCount,
              CONVERT(decimal(19,6),0) UnitsSold,-SUM(l.Quantity) UnitsReturned,
              CONVERT(decimal(19,4),0) GrossSales,CONVERT(decimal(19,4),0) Discounts,
              -SUM(t.Base) Returns,SUM(t.Base) NetUntaxedSales,SUM(t.Tax) NetTax,
              -- Exact tax totals remain known. An undocumented official rounding
              -- is not persisted as an official amount; the context exposes it.
              SUM(COALESCE(t.OfficialTotal,t.ExactTotal)) NetTotalSales,
              SUM(CASE WHEN l.MissingCost=0 THEN COALESCE(l.Cost,0) ELSE 0 END) NetRecognizedCost,
              SUM(CASE WHEN l.MissingCost=0 THEN t.Base-COALESCE(l.Cost,0) ELSE 0 END) GrossProfit,CONVERT(decimal(19,4),0) CreditSales,
              CONVERT(decimal(19,4),0) Collected,SUM(COALESCE(p.Refunded,0)) Refunded,
              SUM(CONVERT(bigint,l.MissingCost)) UnknownCostDocumentCount
            FROM taxes t JOIN lines l ON l.SourceDocumentId=t.SourceDocumentId
              LEFT JOIN payments p ON p.SourceDocumentId=t.SourceDocumentId
            GROUP BY t.BusinessLocalDate;
            """ + DailyDeltaMergeSql + DimensionDeltasSql("SELECT SourceDocumentId FROM #PreservedReturns") + """
            IF @ThroughDate IS NOT NULL EXEC reporting.ProductRotationRefresh @BusinessId,@SourceDocumentId,@SourceDocumentType,
              @ThroughDate,@Version,@Now;
            """ + string.Concat(new[] { "SalesInvoice", "SalesReceipt" }.Select(type => $"""
            SET @SourceDocumentType=N'{type}'; SET @DocumentCount=1;
            SELECT @LocalDate=MIN(d.BusinessLocalDate),@ThroughDate=MAX(d.BusinessLocalDate)
            FROM reporting.SalesReportDocuments d JOIN #PreservedSales s ON s.SourceDocumentId=d.DocumentId
            WHERE d.BusinessId=@BusinessId AND d.TenantId=@TenantId AND d.DocumentType=@SourceDocumentType;
            """ + DimensionDeltasSql("SELECT SourceDocumentId FROM #PreservedSales WHERE SourceDocumentType=@SourceDocumentType") + """
            IF @ThroughDate IS NOT NULL EXEC reporting.ProductRotationRefresh @BusinessId,@SourceDocumentId,@SourceDocumentType,
              @ThroughDate,@Version,@Now;
            """));
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.CommandTimeout = RebuildCommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
