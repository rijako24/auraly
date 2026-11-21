using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProjectionWriter
{
    internal async Task ProjectPaymentCorrectionAsync(SalesReportingSqlSession session,
        Guid tenantId, Guid businessId, Guid correctionId, string correctionType,
        IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken, Guid? dispatchId = null)
    {
        var now = timeProvider.GetUtcNow();
        if (dispatchId is { } dispatch)
        {
            // Include undelivered documents too: their final receipt may be zero.
            // A single bounded set read replaces any per-invoice lookup.
            await using var read = new SqlCommand("""
                SELECT TOP(@Limit) document.SourceDocumentId FROM dbo.DispatchSourceDocuments document
                JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=document.DispatchId AND dispatch.BusinessId=@Business
                  AND dispatch.TenantId=@Tenant
                WHERE document.DispatchId=@Dispatch ORDER BY document.SourceDocumentId;
                """, session.Connection, session.Transaction);
            read.Parameters.AddWithValue("@Limit", Auraly.Contracts.Dispatching.DispatchLimits.MaximumDocuments + 1);
            read.Parameters.AddWithValue("@Tenant", tenantId);
            read.Parameters.AddWithValue("@Business", businessId);
            read.Parameters.AddWithValue("@Dispatch", dispatch);
            var affected = new List<Guid>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) affected.Add(reader.GetGuid(0));
            if (affected.Count > Auraly.Contracts.Dispatching.DispatchLimits.MaximumDocuments)
                throw new InvalidOperationException("El despacho supera el límite de documentos admitido.");
            saleIds = affected;
        }
        await RefreshSalePaymentFactsAsync(session, tenantId, businessId, saleIds, true, now, cancellationToken);
        await UpdateCheckpointAsync(session, businessId, correctionId, correctionType, now, cancellationToken);
    }

    // Reporting is a replaceable read model. Its original sale source and the
    // accepted financial correction remain immutable. Replacing only the tender
    // facts gives the -old/+actual effect without inserting another sale/return.
    // Missing projections are intentional: ProjectSale uses this same refresh
    // when the original source arrives later, so delivery order cannot lose it.
    private static async Task<decimal> RefreshSalePaymentFactsAsync(SalesReportingSqlSession session,
        Guid tenantId, Guid businessId, IReadOnlyCollection<Guid> saleIds, bool updateDaily,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (saleIds.Count == 0) return 0;
        await using var command = new SqlCommand("""
            DECLARE @Input TABLE(DocumentId uniqueidentifier PRIMARY KEY);
            INSERT @Input SELECT DISTINCT value FROM OPENJSON(@Sales) WITH(value uniqueidentifier '$');
            IF EXISTS(SELECT 1 FROM @Input input WHERE NOT EXISTS(
              SELECT 1 FROM dbo.SalesDocuments sale JOIN dbo.Businesses business ON business.BusinessId=sale.BusinessId
              WHERE sale.DocumentId=input.DocumentId AND sale.BusinessId=@Business AND business.TenantId=@Tenant))
              THROW 51619,N'La venta corregida no pertenece al contexto de reporting.',1;

            DECLARE @Dispatch TABLE(DocumentId uniqueidentifier PRIMARY KEY,DispatchId uniqueidentifier);
            IF EXISTS(SELECT document.SourceDocumentId FROM @Input input
              JOIN dbo.DispatchSourceDocuments document ON document.SourceDocumentId=input.DocumentId
              JOIN dbo.WorkSessions session ON session.DispatchId=document.DispatchId AND session.Kind=N'Dispatch'
              JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
              JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=closure.WorkSessionClosureId
                AND financial.SourceDocumentType=N'DispatchCashDifference' AND financial.TenantId=@Tenant AND financial.BusinessId=@Business
                AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
              GROUP BY document.SourceDocumentId HAVING COUNT(*)>1)
              THROW 51619,N'La venta tiene más de un recaudo de despacho confirmado.',1;
            INSERT @Dispatch
            SELECT document.SourceDocumentId,document.DispatchId FROM @Input input
              JOIN dbo.DispatchSourceDocuments document ON document.SourceDocumentId=input.DocumentId
              JOIN dbo.WorkSessions session ON session.DispatchId=document.DispatchId AND session.Kind=N'Dispatch'
              JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
              JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=closure.WorkSessionClosureId
                AND financial.SourceDocumentType=N'DispatchCashDifference' AND financial.TenantId=@Tenant AND financial.BusinessId=@Business
                AND financial.Status IN(N'Posted',N'CommercialEffectsApplied');

            DECLARE @Candidates TABLE(DocumentId uniqueidentifier PRIMARY KEY,DocumentType nvarchar(32),
              BusinessLocalDate date,WorkSessionId uniqueidentifier,CollectionMode nvarchar(16));
            INSERT @Candidates
            SELECT sale.DocumentId,sale.DocumentType,report.BusinessLocalDate,sale.WorkSessionId,sale.CollectionMode
            FROM @Input input JOIN dbo.SalesDocuments sale ON sale.DocumentId=input.DocumentId
              AND sale.BusinessId=@Business AND sale.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
            JOIN reporting.SalesReportDocuments report ON report.DocumentId=sale.DocumentId
              AND report.DocumentType=sale.DocumentType AND report.TenantId=@Tenant AND report.BusinessId=@Business
            WHERE (sale.CollectionMode=N'Dispatch' AND EXISTS(SELECT 1 FROM @Dispatch captured WHERE captured.DocumentId=sale.DocumentId))
              OR (sale.CollectionMode=N'Session' AND EXISTS(
                SELECT 1 FROM dbo.WorkSessionClosures closure
                JOIN dbo.WorkSessionClosureReconciliations correction ON correction.WorkSessionClosureId=closure.WorkSessionClosureId
                JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=correction.ReconciliationId
                  AND financial.SourceDocumentType=N'WorkSessionClosureReconciliation' AND financial.TenantId=@Tenant
                  AND financial.BusinessId=@Business AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
                CROSS APPLY OPENJSON(correction.SnapshotJson,N'$.paymentCorrections') WITH(
                  SourceId uniqueidentifier '$.sourceId',MovementType nvarchar(32) '$.movementType') changed
                WHERE closure.WorkSessionId=sale.WorkSessionId AND changed.SourceId=sale.DocumentId AND changed.MovementType=N'Sale'));
            IF EXISTS(SELECT 1 FROM @Candidates sale CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,sale.DocumentId) payment
              WHERE sale.CollectionMode=N'Session' AND payment.EffectivePaymentReady=0)
              THROW 51619,N'La corrección no tiene evidencia financiera definitiva.',1;

            DECLARE @Wanted TABLE(DocumentId uniqueidentifier,PaymentNumber int,MethodCode nvarchar(32),
              Amount decimal(19,4),Reference nvarchar(160),WorkSessionId uniqueidentifier,PRIMARY KEY(DocumentId,PaymentNumber));
            INSERT @Wanted
            SELECT native.DocumentId,CONVERT(int,ROW_NUMBER() OVER(PARTITION BY native.DocumentId ORDER BY native.Sequence,native.PaymentId)),
              native.MethodCode,native.Amount,native.Reference,native.WorkSessionId
            FROM (
              SELECT sale.DocumentId,payment.PaymentNumber Sequence,CONVERT(uniqueidentifier,NULL) PaymentId,
                payment.EffectiveMethodCode MethodCode,payment.EffectiveAmount Amount,original.Reference,sale.WorkSessionId
              FROM @Candidates sale CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,sale.DocumentId) payment
              JOIN dbo.SalesPayments original ON original.DocumentId=sale.DocumentId AND original.PaymentNumber=payment.PaymentNumber
              WHERE sale.CollectionMode=N'Session' AND payment.EffectiveAmount<>0
              UNION ALL
              SELECT sale.DocumentId,0,payment.PaymentId,payment.MethodCode,payment.Amount,payment.Reference,session.WorkSessionId
              FROM (SELECT DISTINCT DispatchId FROM @Dispatch) captured
              CROSS APPLY dispatch.EffectivePayments(captured.DispatchId) payment
              JOIN dbo.DispatchSourceDocuments document ON document.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
              JOIN @Candidates sale ON sale.DocumentId=document.SourceDocumentId
              JOIN dbo.WorkSessions session ON session.DispatchId=captured.DispatchId AND session.Kind=N'Dispatch'
              WHERE sale.CollectionMode=N'Dispatch' AND payment.ApplicationType=N'InvoicePayment' AND payment.Amount<>0
            ) native;

            DECLARE @Changed TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessLocalDate date,Delta decimal(19,4));
            INSERT @Changed
            SELECT sale.DocumentId,sale.BusinessLocalDate,COALESCE(new.Total,0)-COALESCE(old.Total,0)
            FROM @Candidates sale
            OUTER APPLY(SELECT SUM(Amount) Total FROM @Wanted WHERE DocumentId=sale.DocumentId) new
            OUTER APPLY(SELECT SUM(Amount) Total FROM reporting.SalesReportPaymentFacts
              WHERE SourceDocumentId=sale.DocumentId AND SourceDocumentType=sale.DocumentType AND MovementType=N'Payment') old
            WHERE EXISTS(SELECT PaymentNumber,MethodCode,Amount,Reference,WorkSessionId FROM @Wanted WHERE DocumentId=sale.DocumentId
              EXCEPT SELECT PaymentNumber,MethodCode,Amount,Reference,WorkSessionId FROM reporting.SalesReportPaymentFacts
                WHERE SourceDocumentId=sale.DocumentId AND SourceDocumentType=sale.DocumentType AND MovementType=N'Payment')
              OR EXISTS(SELECT PaymentNumber,MethodCode,Amount,Reference,WorkSessionId FROM reporting.SalesReportPaymentFacts
                WHERE SourceDocumentId=sale.DocumentId AND SourceDocumentType=sale.DocumentType AND MovementType=N'Payment'
              EXCEPT SELECT PaymentNumber,MethodCode,Amount,Reference,WorkSessionId FROM @Wanted WHERE DocumentId=sale.DocumentId);
            DELETE fact FROM reporting.SalesReportPaymentFacts fact JOIN @Changed changed ON changed.DocumentId=fact.SourceDocumentId
              JOIN @Candidates sale ON sale.DocumentId=changed.DocumentId AND sale.DocumentType=fact.SourceDocumentType
            WHERE fact.TenantId=@Tenant AND fact.BusinessId=@Business AND fact.MovementType=N'Payment';
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
              BusinessLocalDate,MovementType,MethodCode,Amount,Reference,WorkSessionId,ProjectedAt)
            SELECT wanted.DocumentId,sale.DocumentType,wanted.PaymentNumber,@Tenant,@Business,sale.BusinessLocalDate,
              N'Payment',wanted.MethodCode,wanted.Amount,wanted.Reference,wanted.WorkSessionId,@Now
            FROM @Wanted wanted JOIN @Changed changed ON changed.DocumentId=wanted.DocumentId
              JOIN @Candidates sale ON sale.DocumentId=wanted.DocumentId;
            UPDATE report SET CollectedAmount=COALESCE(wanted.Total,0),ProjectedAt=@Now
            FROM reporting.SalesReportDocuments report JOIN @Changed changed ON changed.DocumentId=report.DocumentId
            OUTER APPLY(SELECT SUM(Amount) Total FROM @Wanted WHERE DocumentId=changed.DocumentId) wanted
            WHERE report.TenantId=@Tenant AND report.BusinessId=@Business;
            IF @UpdateDaily=1
            BEGIN
              DECLARE @Days TABLE(BusinessLocalDate date PRIMARY KEY,Delta decimal(19,4));
              INSERT @Days SELECT BusinessLocalDate,SUM(Delta) FROM @Changed GROUP BY BusinessLocalDate HAVING SUM(Delta)<>0;
              UPDATE daily SET Collected=daily.Collected+changed.Delta,UpdatedAt=@Now
              FROM reporting.SalesReportDailyTotals daily JOIN @Days changed ON changed.BusinessLocalDate=daily.BusinessLocalDate
              WHERE daily.BusinessId=@Business AND daily.CurrencyCode=N'COP';
              IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Days)
                THROW 51619,N'Falta el consolidado de la venta corregida.',1;
            END;
            SELECT COALESCE(SUM(Delta),0) FROM @Changed;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Tenant", tenantId);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@UpdateDaily", updateDaily);
        command.Parameters.Add("@Sales", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saleIds);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
    }
}
