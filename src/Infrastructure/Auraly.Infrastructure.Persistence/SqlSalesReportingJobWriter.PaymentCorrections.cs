using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static partial class SqlSalesReportingJobWriter
{
    public static Task InsertPaymentCorrectionAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid documentId, string documentType, Guid jobId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!SalesReportingProcessingPolicy.IsPaymentCorrection(documentType))
            throw new ArgumentException("La fuente no es una corrección de medios de pago.", nameof(documentType));
        return InsertFinancialSourcesAsync(connection, transaction, businessId,
            [new(documentId, documentType, jobId)], now, cancellationToken);
    }

    // An explicit reporting rebuild also prepares historical correction sources.
    // It never retries accounting or changes a financial source or its outcome.
    public static async Task BackfillPaymentCorrectionsAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IAuralyIdGenerator ids, DateTimeOffset now, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var read = new SqlCommand("""
                SELECT TOP(200) source.SourceDocumentId,source.SourceDocumentType
                FROM dbo.AccountingSourceDocuments source
                WHERE source.BusinessId=@Business
                  AND source.SourceDocumentType IN(N'WorkSessionClosureReconciliation',N'DispatchCashDifference')
                  AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportingJobs report
                    WHERE report.SourceDocumentId=source.SourceDocumentId
                      AND report.SourceDocumentType=source.SourceDocumentType AND report.SourceVersion=1)
                ORDER BY source.SourceDocumentId,source.SourceDocumentType;
                """, connection, transaction);
            read.Parameters.AddWithValue("@Business", businessId);
            var rows = new List<FinancialReportingJob>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                    rows.Add(new(reader.GetGuid(0), reader.GetString(1), ids.NewId()));
            if (rows.Count == 0) return;
            await InsertFinancialSourcesAsync(connection, transaction, businessId, rows, now, cancellationToken);
        }
    }

    private static async Task InsertFinancialSourcesAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyCollection<FinancialReportingJob> rows, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Input TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(64),JobId uniqueidentifier,
              PRIMARY KEY(DocumentId,DocumentType));
            INSERT @Input SELECT DocumentId,DocumentType,JobId FROM OPENJSON(@Rows)
              WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),JobId uniqueidentifier);
            IF EXISTS(SELECT 1 FROM @Input input WHERE NOT EXISTS(
              SELECT 1 FROM dbo.AccountingSourceDocuments source JOIN dbo.Businesses business
                ON business.BusinessId=source.BusinessId AND business.TenantId=source.TenantId
              WHERE source.SourceDocumentId=input.DocumentId AND source.SourceDocumentType=input.DocumentType
                AND source.BusinessId=@Business))
              THROW 51619,N'La fuente financiera no pertenece a la sede de reporting.',1;
            IF EXISTS(SELECT 1 FROM @Input input JOIN reporting.SalesReportingJobs report
              ON report.SourceDocumentId=input.DocumentId AND report.SourceDocumentType=input.DocumentType AND report.SourceVersion=1
              JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=input.DocumentId AND source.SourceDocumentType=input.DocumentType
              WHERE report.BusinessId<>@Business OR report.SourcePayloadHash<>source.PayloadHash)
              THROW 51619,N'La fuente inmutable de reporting no coincide con la corrección.',1;
            INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
            SELECT input.JobId,@Business,source.SourceDocumentId,source.SourceDocumentType,1,
              source.PayloadHash,source.PayloadJson,N'Pending',0,@Now
            FROM @Input input JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=input.DocumentId
              AND source.SourceDocumentType=input.DocumentType AND source.BusinessId=@Business
            WHERE NOT EXISTS(SELECT 1 FROM reporting.SalesReportingJobs report WITH(UPDLOCK,HOLDLOCK)
              WHERE report.SourceDocumentId=input.DocumentId AND report.SourceDocumentType=input.DocumentType AND report.SourceVersion=1);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record FinancialReportingJob(Guid DocumentId, string DocumentType, Guid JobId);
}
