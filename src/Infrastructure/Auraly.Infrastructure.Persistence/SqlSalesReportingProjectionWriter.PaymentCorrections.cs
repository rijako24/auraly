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
    private static async Task<(decimal Collected, decimal Credit)> RefreshSalePaymentFactsAsync(SalesReportingSqlSession session,
        Guid tenantId, Guid businessId, IReadOnlyCollection<Guid> saleIds, bool updateDaily,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (saleIds.Count == 0) return (0, 0);
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

            -- Materialize the bounded input before the correlated correction
            -- predicates. Otherwise SQL can push those predicates onto a scan
            -- of every sale, even when only one document is being projected.
            DECLARE @SelectedSales TABLE(DocumentId uniqueidentifier PRIMARY KEY,DocumentType nvarchar(32),
              BusinessLocalDate date,WorkSessionId uniqueidentifier,CollectionMode nvarchar(16));
            INSERT @SelectedSales
            SELECT sale.DocumentId,sale.DocumentType,report.BusinessLocalDate,sale.WorkSessionId,sale.CollectionMode
            FROM @Input input JOIN dbo.SalesDocuments sale ON sale.DocumentId=input.DocumentId
              AND sale.BusinessId=@Business AND sale.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
            JOIN reporting.SalesReportDocuments report ON report.DocumentId=sale.DocumentId
              AND report.DocumentType=sale.DocumentType AND report.TenantId=@Tenant AND report.BusinessId=@Business;

            DECLARE @Candidates TABLE(DocumentId uniqueidentifier PRIMARY KEY,DocumentType nvarchar(32),
              BusinessLocalDate date,WorkSessionId uniqueidentifier,CollectionMode nvarchar(16));
            INSERT @Candidates
            SELECT sale.DocumentId,sale.DocumentType,sale.BusinessLocalDate,sale.WorkSessionId,sale.CollectionMode
            FROM @SelectedSales sale
            WHERE EXISTS(SELECT 1 FROM @Dispatch captured WHERE captured.DocumentId=sale.DocumentId)
              OR EXISTS(SELECT 1 FROM accounting.SalePaymentCorrections correction
                WHERE correction.TenantId=@Tenant AND correction.BusinessId=@Business
                  AND correction.DocumentId=sale.DocumentId
                  AND correction.PostingStatus IN(N'Posted',N'CommercialEffectsApplied'))
              OR (sale.CollectionMode=N'Session' AND EXISTS(
                SELECT 1 FROM dbo.WorkSessionClosures closure
                JOIN dbo.WorkSessionClosureReconciliations correction ON correction.WorkSessionClosureId=closure.WorkSessionClosureId
                JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=correction.ReconciliationId
                  AND financial.SourceDocumentType=N'WorkSessionClosureReconciliation' AND financial.TenantId=@Tenant
                  AND financial.BusinessId=@Business AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
                CROSS APPLY OPENJSON(correction.SnapshotJson,N'$.paymentCorrections') WITH(
                  SourceId uniqueidentifier '$.sourceId',MovementType nvarchar(32) '$.movementType') changed
                WHERE closure.WorkSessionId=sale.WorkSessionId AND changed.SourceId=sale.DocumentId AND changed.MovementType IN(N'Sale',N'CreditSale')));
            IF NOT EXISTS(SELECT 1 FROM @Candidates)
            BEGIN
              SELECT CONVERT(decimal(19,4),0),CONVERT(decimal(19,4),0);
              RETURN;
            END;
            IF EXISTS(SELECT 1 FROM @Candidates sale CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,sale.DocumentId) payment
              WHERE payment.CollectedOutsideDispatch=1 AND payment.EffectivePaymentRecorded=0)
              THROW 51619,N'El medio de pago corregido no tiene un registro confirmado.',1;

            DECLARE @CreditCollections TABLE(DocumentId uniqueidentifier,PaymentId uniqueidentifier,
              MethodCode nvarchar(32),Amount decimal(19,4),Reference nvarchar(160),WorkSessionId uniqueidentifier,
              PRIMARY KEY(DocumentId,PaymentId));
            DECLARE @CreditSalesJson nvarchar(max)=(SELECT DocumentId FROM @Candidates FOR JSON PATH);
            INSERT @CreditCollections
            SELECT payment.DocumentId,payment.PaymentId,payment.MethodCode,payment.Amount,payment.Reference,payment.WorkSessionId
            FROM accounting.SaleCreditCollectionFacts(@Tenant,@Business,@CreditSalesJson) payment;

            DECLARE @CreditBalances TABLE(DocumentId uniqueidentifier PRIMARY KEY,Amount decimal(19,4),Delta decimal(19,4));
            INSERT @CreditBalances
            SELECT selected.DocumentId,sale.CreditAmount-COALESCE(collected.Amount,0),
              sale.CreditAmount-COALESCE(collected.Amount,0)-report.CreditAmount
            FROM @Candidates selected JOIN dbo.SalesDocuments sale ON sale.DocumentId=selected.DocumentId
            JOIN reporting.SalesReportDocuments report ON report.DocumentId=selected.DocumentId AND report.DocumentType=selected.DocumentType
            OUTER APPLY(SELECT SUM(Amount) Amount FROM @CreditCollections WHERE DocumentId=selected.DocumentId) collected;
            IF EXISTS(SELECT 1 FROM @CreditBalances WHERE Amount<0)
              THROW 51619,N'Los recaudos corregidos superan el crédito original de la factura.',1;

            DECLARE @Wanted TABLE(DocumentId uniqueidentifier,PaymentNumber int,MethodCode nvarchar(32),
              Amount decimal(19,4),Reference nvarchar(160),WorkSessionId uniqueidentifier,PRIMARY KEY(DocumentId,PaymentNumber));
            INSERT @Wanted
            SELECT native.DocumentId,CONVERT(int,ROW_NUMBER() OVER(PARTITION BY native.DocumentId ORDER BY native.Sequence,native.PaymentId)),
              native.MethodCode,native.Amount,native.Reference,native.WorkSessionId
            FROM (
              SELECT sale.DocumentId,payment.PaymentNumber Sequence,CONVERT(uniqueidentifier,NULL) PaymentId,
                payment.EffectiveMethodCode MethodCode,payment.EffectiveAmount Amount,original.Reference,
                CASE WHEN sale.CollectionMode=N'Session' THEN sale.WorkSessionId END WorkSessionId
              FROM @Candidates sale CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,sale.DocumentId) payment
              JOIN dbo.SalesPayments original ON original.DocumentId=sale.DocumentId AND original.PaymentNumber=payment.PaymentNumber
              WHERE payment.CollectedOutsideDispatch=1 AND payment.EffectiveAmount<>0
              UNION ALL
              SELECT sale.DocumentId,0,payment.PaymentId,payment.MethodCode,payment.Amount,payment.Reference,session.WorkSessionId
              FROM (SELECT DISTINCT DispatchId FROM @Dispatch) captured
              CROSS APPLY dispatch.EffectivePayments(captured.DispatchId) payment
              JOIN dbo.DispatchSourceDocuments document ON document.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
              JOIN @Candidates sale ON sale.DocumentId=document.SourceDocumentId
              JOIN dbo.WorkSessions session ON session.DispatchId=captured.DispatchId AND session.Kind=N'Dispatch'
              WHERE sale.CollectionMode=N'Dispatch' AND payment.ApplicationType=N'InvoicePayment' AND payment.Amount<>0
              UNION ALL
              SELECT credit.DocumentId,2147483647,credit.PaymentId,credit.MethodCode,credit.Amount,credit.Reference,credit.WorkSessionId
              FROM @CreditCollections credit
            ) native;

            DECLARE @Changed TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessLocalDate date,Delta decimal(19,4),CreditDelta decimal(19,4));
            INSERT @Changed
            SELECT sale.DocumentId,sale.BusinessLocalDate,COALESCE(new.Total,0)-COALESCE(old.Total,0),credit.Delta
            FROM @Candidates sale
            JOIN @CreditBalances credit ON credit.DocumentId=sale.DocumentId
            OUTER APPLY(SELECT SUM(Amount) Total FROM @Wanted WHERE DocumentId=sale.DocumentId) new
            OUTER APPLY(SELECT SUM(Amount) Total FROM reporting.SalesReportPaymentFacts
              WHERE SourceDocumentId=sale.DocumentId AND SourceDocumentType=sale.DocumentType AND MovementType=N'Payment') old
            WHERE credit.Delta<>0 OR EXISTS(SELECT PaymentNumber,MethodCode,Amount,Reference,WorkSessionId FROM @Wanted WHERE DocumentId=sale.DocumentId
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
            DELETE fact FROM reporting.SalesReportPaymentFacts fact JOIN @Changed changed ON changed.DocumentId=fact.SourceDocumentId
              JOIN @Candidates sale ON sale.DocumentId=changed.DocumentId AND sale.DocumentType=fact.SourceDocumentType
            WHERE fact.TenantId=@Tenant AND fact.BusinessId=@Business AND fact.MovementType=N'Credit';
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
              BusinessLocalDate,MovementType,MethodCode,Amount,WorkSessionId,ProjectedAt)
            SELECT sale.DocumentId,sale.DocumentType,0,@Tenant,@Business,sale.BusinessLocalDate,N'Credit',N'Credit',credit.Amount,sale.WorkSessionId,@Now
            FROM @Candidates sale JOIN @Changed changed ON changed.DocumentId=sale.DocumentId
            JOIN @CreditBalances credit ON credit.DocumentId=sale.DocumentId WHERE credit.Amount>0;
            UPDATE report SET CollectedAmount=COALESCE(wanted.Total,0),CreditAmount=credit.Amount,ProjectedAt=@Now
            FROM reporting.SalesReportDocuments report JOIN @Changed changed ON changed.DocumentId=report.DocumentId
            JOIN @CreditBalances credit ON credit.DocumentId=changed.DocumentId
            OUTER APPLY(SELECT SUM(Amount) Total FROM @Wanted WHERE DocumentId=changed.DocumentId) wanted
            WHERE report.TenantId=@Tenant AND report.BusinessId=@Business;
            IF @UpdateDaily=1
            BEGIN
              DECLARE @Days TABLE(BusinessLocalDate date PRIMARY KEY,Delta decimal(19,4),CreditDelta decimal(19,4));
              INSERT @Days SELECT BusinessLocalDate,SUM(Delta),SUM(CreditDelta) FROM @Changed GROUP BY BusinessLocalDate
                HAVING SUM(Delta)<>0 OR SUM(CreditDelta)<>0;
              UPDATE daily SET Collected=daily.Collected+changed.Delta,CreditSales=daily.CreditSales+changed.CreditDelta,UpdatedAt=@Now
              FROM reporting.SalesReportDailyTotals daily JOIN @Days changed ON changed.BusinessLocalDate=daily.BusinessLocalDate
              WHERE daily.BusinessId=@Business AND daily.CurrencyCode=N'COP';
              IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Days)
                THROW 51619,N'Falta el consolidado de la venta corregida.',1;
            END;
            SELECT COALESCE(SUM(Delta),0),COALESCE(SUM(CreditDelta),0) FROM @Changed;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Tenant", tenantId);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@UpdateDaily", updateDaily);
        command.Parameters.Add("@Sales", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saleIds);
        await using var result = await command.ExecuteReaderAsync(cancellationToken);
        if (!await result.ReadAsync(cancellationToken))
            throw new InvalidOperationException("No se obtuvo el resultado autoritativo de la proyección del recaudo.");
        return (result.GetDecimal(0), result.GetDecimal(1));
    }
}
