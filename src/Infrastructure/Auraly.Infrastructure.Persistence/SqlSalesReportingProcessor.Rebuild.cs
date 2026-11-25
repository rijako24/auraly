using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProcessor
{
    public async Task<long> RebuildAsync(Guid tenantId,Guid businessId,CancellationToken cancellationToken)
    {
        if(tenantId==Guid.Empty||businessId==Guid.Empty)throw new ArgumentException("Tenant y sede son obligatorios.");
        await using var connection=connections.Create();await connection.OpenAsync(cancellationToken);
        await using(var authorize=new SqlCommand("SELECT COUNT(*) FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant;",connection))
        {
            authorize.Parameters.AddWithValue("@Business",businessId);authorize.Parameters.AddWithValue("@Tenant",tenantId);
            if(Convert.ToInt32(await authorize.ExecuteScalarAsync(cancellationToken))!=1)
                throw new InvalidOperationException("La sede no pertenece al tenant seleccionado.");
        }
        // The session lock serializes only Reporting. Operational documents remain writable.
        await AcquireReportingLockAsync(connection,null,businessId,"Exclusive","Session",cancellationToken);
        try
        {
            // Create outside the parameterized reset batch: a temporary table
            // created inside sp_executesql would disappear before the page read.
            await using(var create=new SqlCommand("""
                CREATE TABLE #RebuildSources(Sequence bigint NOT NULL PRIMARY KEY,
                  SourceDocumentId uniqueidentifier NOT NULL,SourceDocumentType nvarchar(32) COLLATE DATABASE_DEFAULT NOT NULL,SourceVersion bigint NOT NULL);
                CREATE INDEX IX_RebuildSources_Document ON #RebuildSources(SourceDocumentId,SourceDocumentType);
                CREATE TABLE #PreservedSales(SourceDocumentId uniqueidentifier NOT NULL PRIMARY KEY,
                  SourceDocumentType nvarchar(32) COLLATE DATABASE_DEFAULT NOT NULL);
                CREATE TABLE #PreservedReturns(SourceDocumentId uniqueidentifier NOT NULL PRIMARY KEY,
                  OriginalSaleDocumentId uniqueidentifier NOT NULL);
                """,connection))
                await create.ExecuteNonQueryAsync(cancellationToken);
            await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken))
            {
                await SqlSalesReportingJobWriter.BackfillServiceInvoicesAsync(connection,transaction,businessId,ids,timeProvider.GetUtcNow(),cancellationToken);
                await SqlSalesReportingJobWriter.BackfillPaymentCorrectionsAsync(connection,transaction,businessId,ids,timeProvider.GetUtcNow(),cancellationToken);
                await using var reset=new SqlCommand("""
                    INSERT #RebuildSources(Sequence,SourceDocumentId,SourceDocumentType,SourceVersion)
                    SELECT ROW_NUMBER() OVER(ORDER BY CASE WHEN r.SourceDocumentType IN(N'SalesReturn',N'PurchaseReturn') THEN 1 ELSE 0 END,
                      r.CreatedAt,r.SourceDocumentId,r.SourceDocumentType,r.SourceVersion) Sequence,
                      r.SourceDocumentId,r.SourceDocumentType,r.SourceVersion
                    FROM reporting.SalesReportingJobs r
                    LEFT JOIN dbo.DocumentProcessingJobs j ON j.JobId=r.SourceDocumentProcessingJobId
                    WHERE r.BusinessId=@Business AND (r.SourcePayloadJson IS NOT NULL OR j.Status=N'Completed')
                      AND (r.SourceDocumentType NOT IN(N'WorkSessionClosureReconciliation',N'DispatchCashDifference') OR EXISTS(
                        SELECT 1 FROM dbo.AccountingPostingJobs financial WHERE financial.SourceDocumentId=r.SourceDocumentId
                          AND financial.SourceDocumentType=r.SourceDocumentType AND financial.BusinessId=r.BusinessId
                          AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')));
                    WITH lines AS(
                      SELECT f.SourceDocumentId,f.SourceDocumentType,MIN(f.BusinessLocalDate) LocalDate,
                        COUNT_BIG(*) LineCount,SUM(f.UntaxedAmount) Base,SUM(f.TaxAmount) Tax,SUM(f.TotalAmount) Total
                      FROM reporting.SalesReportLineFacts f WHERE f.BusinessId=@Business AND f.TenantId=@Tenant
                        AND f.SourceDocumentType IN(N'SalesInvoice',N'SalesReceipt')
                      GROUP BY f.SourceDocumentId,f.SourceDocumentType
                      HAVING COUNT(DISTINCT f.OriginalSaleDocumentId)=1 AND MIN(CONVERT(char(36),f.OriginalSaleDocumentId))=CONVERT(char(36),f.SourceDocumentId)
                        AND COUNT(DISTINCT f.BusinessLocalDate)=1
                    ), taxes AS(
                      SELECT f.SourceDocumentId,f.SourceDocumentType,MIN(f.BusinessLocalDate) LocalDate,
                        COUNT_BIG(*) TaxCount,SUM(f.TaxableAmount) Base,SUM(f.TaxAmount) Tax,SUM(f.TotalAmount) Total
                      FROM reporting.SalesReportTaxFacts f WHERE f.BusinessId=@Business AND f.TenantId=@Tenant
                        AND f.SourceDocumentType IN(N'SalesInvoice',N'SalesReceipt')
                      GROUP BY f.SourceDocumentId,f.SourceDocumentType HAVING COUNT(DISTINCT f.BusinessLocalDate)=1
                    )
                    INSERT #PreservedSales(SourceDocumentId,SourceDocumentType)
                    SELECT original.DocumentId,original.DocumentType FROM reporting.SalesReportDocuments original
                    JOIN lines l ON l.SourceDocumentId=original.DocumentId AND l.SourceDocumentType=original.DocumentType AND l.LocalDate=original.BusinessLocalDate
                    JOIN taxes t ON t.SourceDocumentId=original.DocumentId AND t.SourceDocumentType=original.DocumentType AND t.LocalDate=original.BusinessLocalDate
                    WHERE original.BusinessId=@Business AND original.TenantId=@Tenant AND original.CurrencyCode=N'COP'
                      AND ABS(original.UntaxedAmount-t.Base)<=0.0001 AND original.TaxAmount=t.Tax
                      AND ABS(original.TotalAmount-original.RoundingAdjustmentAmount-t.Total)<=0.0001
                      -- Each stored line/tax group is rounded independently to four
                      -- decimals. Retain the evidence only within that error bound.
                      AND ABS(l.Base-t.Base)<=0.00005*(l.LineCount+t.TaxCount) AND l.Tax=t.Tax
                      AND ABS(l.Total-t.Total)<=0.00005*(l.LineCount+t.TaxCount)
                      AND NOT EXISTS(SELECT 1 FROM #RebuildSources source WHERE source.SourceDocumentId=original.DocumentId AND source.SourceDocumentType=original.DocumentType)
                      AND NOT EXISTS(SELECT 1 FROM dbo.DocumentProcessingPayloads p WHERE p.DocumentId=original.DocumentId)
                      AND NOT EXISTS(SELECT 1 FROM dbo.SalesDocuments d WHERE d.DocumentId=original.DocumentId)
                      AND NOT EXISTS(SELECT 1 FROM reporting.ServiceSalesReportLineFacts f WHERE f.SourceDocumentId=original.DocumentId)
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f WHERE f.SourceDocumentId=original.DocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>original.DocumentType OR f.MovementType<>N'Sale'))
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportTaxFacts f WHERE f.SourceDocumentId=original.DocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>original.DocumentType
                          OR f.OriginalSaleDocumentId IS NOT NULL AND f.OriginalSaleDocumentId<>original.DocumentId))
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportPaymentFacts f WHERE f.SourceDocumentId=original.DocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>original.DocumentType));
                    IF NOT EXISTS(SELECT 1 FROM #RebuildSources) AND NOT EXISTS(SELECT 1 FROM #PreservedSales)
                      THROW 51621,N'La sede no tiene fuentes inmutables ni histórico conciliable para reconstruir.',1;
                    -- Imported returns can be retained as existing Reporting evidence,
                    -- without fabricating operational roots, payments or source jobs.
                    -- Require one scoped original sale and reconciled line/tax amounts.
                    WITH lines AS(
                      SELECT f.SourceDocumentId,CONVERT(uniqueidentifier,MIN(CONVERT(char(36),f.OriginalSaleDocumentId))) OriginalId,
                        MIN(f.BusinessLocalDate) LocalDate,SUM(f.UntaxedAmount) Base,SUM(f.TaxAmount) Tax,SUM(f.TotalAmount) Total
                      FROM reporting.SalesReportLineFacts f
                      WHERE f.BusinessId=@Business AND f.TenantId=@Tenant AND f.SourceDocumentType=N'SalesReturn'
                        AND NOT EXISTS(SELECT 1 FROM #RebuildSources s WHERE s.SourceDocumentId=f.SourceDocumentId AND s.SourceDocumentType=f.SourceDocumentType)
                        AND NOT EXISTS(SELECT 1 FROM dbo.SalesReturns operational WHERE operational.ReturnId=f.SourceDocumentId)
                        AND NOT EXISTS(SELECT 1 FROM dbo.DocumentProcessingPayloads p WHERE p.DocumentId=f.SourceDocumentId)
                        AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments a WHERE a.SourceDocumentId=f.SourceDocumentId)
                      GROUP BY f.SourceDocumentId
                      HAVING COUNT(DISTINCT f.OriginalSaleDocumentId)=1 AND COUNT(DISTINCT f.BusinessLocalDate)=1
                    ), taxes AS(
                      SELECT f.SourceDocumentId,MIN(f.BusinessLocalDate) LocalDate,
                        SUM(f.TaxableAmount) Base,SUM(f.TaxAmount) Tax,SUM(f.TotalAmount) Total
                      FROM reporting.SalesReportTaxFacts f
                      WHERE f.BusinessId=@Business AND f.TenantId=@Tenant AND f.SourceDocumentType=N'SalesReturn'
                      GROUP BY f.SourceDocumentId HAVING COUNT(DISTINCT f.BusinessLocalDate)=1
                    )
                    INSERT #PreservedReturns(SourceDocumentId,OriginalSaleDocumentId)
                    SELECT l.SourceDocumentId,l.OriginalId FROM lines l JOIN taxes t ON t.SourceDocumentId=l.SourceDocumentId
                      AND t.LocalDate=l.LocalDate AND ABS(t.Base-l.Base)<=0.0001 AND t.Tax=l.Tax AND ABS(t.Total-l.Total)<=0.0001
                    JOIN reporting.SalesReportDocuments original ON original.DocumentId=l.OriginalId
                      AND original.BusinessId=@Business AND original.TenantId=@Tenant
                      AND original.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
                    WHERE (EXISTS(SELECT 1 FROM #RebuildSources s WHERE s.SourceDocumentId=original.DocumentId AND s.SourceDocumentType=original.DocumentType)
                        OR EXISTS(SELECT 1 FROM #PreservedSales s WHERE s.SourceDocumentId=original.DocumentId AND s.SourceDocumentType=original.DocumentType))
                      AND NOT EXISTS(SELECT 1 FROM reporting.ServiceSalesReportLineFacts service WHERE service.SourceDocumentId=l.SourceDocumentId)
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f WHERE f.SourceDocumentId=l.SourceDocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>N'SalesReturn' OR f.MovementType<>N'Return'))
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportTaxFacts f WHERE f.SourceDocumentId=l.SourceDocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>N'SalesReturn'
                          OR f.OriginalSaleDocumentId IS NOT NULL AND f.OriginalSaleDocumentId<>l.OriginalId))
                      AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportPaymentFacts f WHERE f.SourceDocumentId=l.SourceDocumentId
                        AND (f.BusinessId<>@Business OR f.TenantId<>@Tenant OR f.SourceDocumentType<>N'SalesReturn'));
                    -- Every other source-bearing projection still requires a complete
                    -- manifest. Incomplete or conflicting imports fail before reset.
                    IF EXISTS(
                      SELECT 1 FROM (
                        SELECT DocumentId SourceDocumentId,DocumentType SourceDocumentType
                          FROM reporting.SalesReportDocuments WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.SalesReportTaxFacts WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.SalesReportPaymentFacts WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.SalesReportLineFacts WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.ServiceSalesReportLineFacts WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.PurchaseReportDocuments WHERE BusinessId=@Business
                        UNION ALL SELECT SourceDocumentId,SourceDocumentType
                          FROM reporting.PurchaseReportLineFacts WHERE BusinessId=@Business
                      ) existing
                      WHERE NOT EXISTS(SELECT 1 FROM #RebuildSources source
                        WHERE source.SourceDocumentId=existing.SourceDocumentId
                          AND source.SourceDocumentType=existing.SourceDocumentType)
                        AND NOT (existing.SourceDocumentType=N'SalesReturn' AND EXISTS(
                          SELECT 1 FROM #PreservedReturns retained WHERE retained.SourceDocumentId=existing.SourceDocumentId))
                        AND NOT EXISTS(SELECT 1 FROM #PreservedSales retained WHERE retained.SourceDocumentId=existing.SourceDocumentId
                          AND retained.SourceDocumentType=existing.SourceDocumentType))
                      THROW 51626,N'La sede conserva histórico de reportes sin fuentes suficientes para reconstruirlo. No se modificó el histórico.',1;
                    -- Keep the original headers durable across an interrupted rebuild.
                    -- Remove only the contribution of regenerable return facts currently
                    -- present; replay will add it once. The imported remainder survives.
                    SELECT returned.OriginalDocumentId, SUM(-taxes.Base) Untaxed,SUM(-taxes.Tax) Tax,
                      SUM(returned.TotalAmount) Total,SUM(-COALESCE(cost.Amount,0)) Cost
                    INTO #RegenerableReturns
                    FROM dbo.SalesReturns returned
                    JOIN (SELECT f.SourceDocumentId,SUM(f.TaxableAmount) Base,SUM(f.TaxAmount) Tax
                      FROM reporting.SalesReportTaxFacts f JOIN #RebuildSources source ON source.SourceDocumentId=f.SourceDocumentId
                        AND source.SourceDocumentType=f.SourceDocumentType
                      WHERE f.BusinessId=@Business AND f.SourceDocumentType=N'SalesReturn' GROUP BY f.SourceDocumentId) taxes
                      ON taxes.SourceDocumentId=returned.ReturnId
                    LEFT JOIN (SELECT SourceDocumentId,SUM(RecognizedCostAmount) Amount FROM reporting.SalesReportLineFacts
                      WHERE BusinessId=@Business AND SourceDocumentType=N'SalesReturn' GROUP BY SourceDocumentId) cost
                      ON cost.SourceDocumentId=returned.ReturnId
                    WHERE returned.BusinessId=@Business AND (EXISTS(SELECT 1 FROM #PreservedReturns retained
                      WHERE retained.OriginalSaleDocumentId=returned.OriginalDocumentId)
                      OR EXISTS(SELECT 1 FROM #PreservedSales retained WHERE retained.SourceDocumentId=returned.OriginalDocumentId))
                    GROUP BY returned.OriginalDocumentId;
                    IF EXISTS(SELECT 1 FROM reporting.SalesReportDocuments original JOIN #RegenerableReturns r ON r.OriginalDocumentId=original.DocumentId
                      WHERE original.BusinessId=@Business AND (original.ReturnedUntaxedAmount<r.Untaxed OR original.ReturnedTaxAmount<r.Tax
                        OR original.ReturnedTotalAmount<r.Total OR original.ReturnedCostAmount<r.Cost))
                      THROW 51628,N'Las devoluciones conservadas no concilian con su cabecera de venta. No se modificó el histórico.',1;
                    UPDATE original SET ReturnedUntaxedAmount=original.ReturnedUntaxedAmount-r.Untaxed,
                      ReturnedTaxAmount=original.ReturnedTaxAmount-r.Tax,ReturnedTotalAmount=original.ReturnedTotalAmount-r.Total,
                      ReturnedCostAmount=original.ReturnedCostAmount-r.Cost
                    FROM reporting.SalesReportDocuments original JOIN #RegenerableReturns r ON r.OriginalDocumentId=original.DocumentId
                    WHERE original.BusinessId=@Business;
                    DELETE f FROM reporting.SalesReportPaymentFacts f WHERE f.BusinessId=@Business
                      AND NOT (f.SourceDocumentType=N'SalesReturn' AND EXISTS(SELECT 1 FROM #PreservedReturns r WHERE r.SourceDocumentId=f.SourceDocumentId))
                      AND NOT EXISTS(SELECT 1 FROM #PreservedSales s WHERE s.SourceDocumentId=f.SourceDocumentId AND s.SourceDocumentType=f.SourceDocumentType);
                    DELETE f FROM reporting.SalesReportTaxFacts f WHERE f.BusinessId=@Business
                      AND NOT (f.SourceDocumentType=N'SalesReturn' AND EXISTS(SELECT 1 FROM #PreservedReturns r WHERE r.SourceDocumentId=f.SourceDocumentId))
                      AND NOT EXISTS(SELECT 1 FROM #PreservedSales s WHERE s.SourceDocumentId=f.SourceDocumentId AND s.SourceDocumentType=f.SourceDocumentType);
                    DELETE reporting.ServiceSalesReportLineFacts WHERE BusinessId=@Business;
                    DELETE f FROM reporting.SalesReportLineFacts f WHERE f.BusinessId=@Business
                      AND NOT (f.SourceDocumentType=N'SalesReturn' AND EXISTS(SELECT 1 FROM #PreservedReturns r WHERE r.SourceDocumentId=f.SourceDocumentId))
                      AND NOT EXISTS(SELECT 1 FROM #PreservedSales s WHERE s.SourceDocumentId=f.SourceDocumentId AND s.SourceDocumentType=f.SourceDocumentType);
                    DELETE original FROM reporting.SalesReportDocuments original WHERE original.BusinessId=@Business
                      AND NOT EXISTS(SELECT 1 FROM #PreservedReturns retained WHERE retained.OriginalSaleDocumentId=original.DocumentId)
                      AND NOT EXISTS(SELECT 1 FROM #PreservedSales s WHERE s.SourceDocumentId=original.DocumentId);
                    DELETE reporting.SalesReportDailyTotals WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportDailyDimensionTotals WHERE BusinessId=@Business;
                    DELETE reporting.PurchaseReportLineFacts WHERE BusinessId=@Business;
                    DELETE reporting.PurchaseReportDocuments WHERE BusinessId=@Business;
                    DELETE reporting.ProductDailyRotationSnapshots WHERE BusinessId=@Business;
                    DELETE reporting.ProductRotationSnapshots WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportingCheckpoints WHERE BusinessId=@Business;
                    INSERT reporting.SalesReportingCheckpoints(BusinessId,ProjectionVersion,LastSourceDocumentId,LastSourceDocumentType,LastProjectedAt,RebuildInProgress)
                    SELECT TOP(1) @Business,@Version,SourceDocumentId,SourceDocumentType,@Now,1 FROM(
                      SELECT Sequence,SourceDocumentId,SourceDocumentType FROM #RebuildSources
                      UNION ALL SELECT CONVERT(bigint,9223372036854775807),SourceDocumentId,SourceDocumentType FROM #PreservedSales
                    ) checkpointSource ORDER BY Sequence,SourceDocumentId;
                    UPDATE r SET Status=N'Pending',StartedAt=NULL,CompletedAt=NULL,LastError=NULL
                    FROM reporting.SalesReportingJobs r JOIN #RebuildSources s ON s.SourceDocumentId=r.SourceDocumentId
                      AND s.SourceDocumentType=r.SourceDocumentType AND s.SourceVersion=r.SourceVersion WHERE r.BusinessId=@Business;
                    """,connection,transaction);
                reset.Parameters.AddWithValue("@Business",businessId);reset.Parameters.AddWithValue("@Version",SqlSalesReportingProjectionWriter.ProjectionVersion);
                reset.Parameters.AddWithValue("@Tenant",tenantId);
                reset.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
                reset.CommandTimeout=SqlSalesReportingProjectionWriter.RebuildCommandTimeoutSeconds;
                await reset.ExecuteNonQueryAsync(cancellationToken);await transaction.CommitAsync(cancellationToken);
            }
            long cursor=0;
            while(true)
            {
                var batch=new List<(long Sequence,Guid Document,string Type,long Version)>();
                await using(var read=new SqlCommand("SELECT TOP(200) Sequence,SourceDocumentId,SourceDocumentType,SourceVersion FROM #RebuildSources WHERE Sequence>@After ORDER BY Sequence;",connection))
                {
                    read.Parameters.AddWithValue("@After",cursor);
                    await using var reader=await read.ExecuteReaderAsync(cancellationToken);
                    while(await reader.ReadAsync(cancellationToken))batch.Add((reader.GetInt64(0),reader.GetGuid(1),reader.GetString(2),reader.GetInt64(3)));
                }
                if(batch.Count==0)break;
                // Every item is an independent canonical Reporting transaction. No operational handler is invoked.
                foreach(var source in batch)
                {
                    await ProcessSourceAsync(connection,source.Document,source.Type,businessId,source.Version,true,cancellationToken);
                    cursor=source.Sequence;
                }
            }
            await using var finalTransaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
            await projectionWriter.RestorePreservedHistoryAsync(new(connection,finalTransaction),tenantId,businessId,cancellationToken);
            await using var complete=new SqlCommand("UPDATE reporting.SalesReportingCheckpoints SET RebuildInProgress=0 WHERE BusinessId=@Business AND ProjectionVersion=@Version;",connection,finalTransaction);
            complete.Parameters.AddWithValue("@Business",businessId);complete.Parameters.AddWithValue("@Version",SqlSalesReportingProjectionWriter.ProjectionVersion);
            if(await complete.ExecuteNonQueryAsync(cancellationToken)!=1)throw new InvalidOperationException("La reconstrucción no pudo cerrar su checkpoint.");
            await finalTransaction.CommitAsync(cancellationToken);
            return cursor;
        }
        finally
        {
            await using var release=new SqlCommand("EXEC sys.sp_releaseapplock @Resource,@LockOwner=N'Session';",connection);
            release.Parameters.AddWithValue("@Resource",ReportingLockResource(businessId));
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal const string TransactionLockSql="""
        DECLARE @ReportingLockResult int;
        EXEC @ReportingLockResult=sys.sp_getapplock @Resource=@ReportingLockResource,@LockMode=@ReportingLockMode,@LockOwner=N'Transaction',@LockTimeout=5000;
        IF @ReportingLockResult<0 THROW 51620,N'La proyección está ocupada; vuelve a intentar cuando termine la operación en curso.',1;
        """;

    internal static string ReportingLockResource(Guid businessId)=>$"reporting:{businessId:D}";
    private static async Task AcquireReportingLockAsync(SqlConnection connection,SqlTransaction? transaction,
        Guid businessId,string mode,string owner,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource,@LockMode=@Mode,@LockOwner=@Owner,@LockTimeout=5000;
            IF @Result<0 THROW 51620,N'La proyección está ocupada; vuelve a intentar cuando termine la operación en curso.',1;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Resource",ReportingLockResource(businessId));command.Parameters.AddWithValue("@Mode",mode);command.Parameters.AddWithValue("@Owner",owner);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
