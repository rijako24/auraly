using System.Data;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReportingProcessor(
    SqlServerConnectionFactory connections,
    SqlSalesReportingProjectionWriter projectionWriter,
    TimeProvider timeProvider)
{
    public async Task ProcessAsync(
        Guid documentId,
        string documentType,
        Guid businessId,
        long sourceVersion,
        CancellationToken cancellationToken)
    {
        if (!SalesReportingProcessingPolicy.Supports(documentType)) return;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var committed = false;
        try
        {
            var source = await LockSourceAsync(
                connection, transaction, documentId, documentType, businessId,
                sourceVersion,cancellationToken);
            if (source is null)
                throw new InvalidOperationException(
                    "The completed document has no immutable reporting source.");
            if (source.Status == "Projected")
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var startedAt = timeProvider.GetUtcNow();
            await using (var start = new SqlCommand("""
                UPDATE reporting.SalesReportingJobs
                SET Status=N'Processing',AttemptCount=AttemptCount+1,
                    StartedAt=@StartedAt,CompletedAt=NULL,LastError=NULL
                WHERE SourceDocumentId=@DocumentId
                  AND SourceDocumentType=@DocumentType
                  AND BusinessId=@BusinessId
                  AND SourceVersion=@SourceVersion
                  AND Status IN(N'Pending',N'Failed');
                """, connection, transaction))
            {
                start.Parameters.AddWithValue("@DocumentId", documentId);
                start.Parameters.AddWithValue("@DocumentType", documentType);
                start.Parameters.AddWithValue("@BusinessId", businessId);
                start.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                start.Parameters.AddWithValue("@StartedAt", startedAt);
                if (await start.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The sales reporting job could not be acquired.");
            }
            transaction.Save("BeforeProjection");

            try
            {
                var session = new SalesReportingSqlSession(connection, transaction);
                if (documentType is "SalesInvoice" or "SalesReceipt")
                    await projectionWriter.ProjectSaleAsync(
                        session, PosSaleContractSerializer.Deserialize(source.Payload),
                        cancellationToken);
                else if (documentType == ServiceInvoiceDocumentTypes.ServiceInvoice)
                    await projectionWriter.ProjectServiceInvoiceAsync(
                        session, ServiceInvoiceSnapshotSerializer.Deserialize(source.Payload),
                        cancellationToken);
                else
                if (documentType == "SalesReturn")
                    await projectionWriter.ProjectReturnAsync(session,
                        SalesReturnContractSerializer.Deserialize(source.Payload),cancellationToken);
                else if(documentType=="RouteVisit")
                    await projectionWriter.ProjectVisitAsync(session,source.Payload,sourceVersion,cancellationToken);
                else if(documentType=="CommercialCoveragePlan")
                    await projectionWriter.ProjectCoverageAsync(session,source.Payload,sourceVersion,cancellationToken);
                else if(documentType=="GoodsReceipt")
                    await projectionWriter.ProjectGoodsReceiptAsync(session,
                        GoodsReceiptContractSerializer.Deserialize(source.Payload),cancellationToken);
                else if(documentType=="PurchaseReturn")
                    await projectionWriter.ProjectPurchaseReturnAsync(session,
                        PurchaseReturnContractSerializer.Deserialize(source.Payload),cancellationToken);
                else
                    await projectionWriter.ProjectOrderAsync(session,source.Payload,sourceVersion,cancellationToken);

                await using var complete = new SqlCommand("""
                    UPDATE reporting.SalesReportingJobs
                    SET Status=N'Projected',CompletedAt=SYSDATETIMEOFFSET(),LastError=NULL
                    WHERE SourceDocumentId=@DocumentId
                      AND SourceDocumentType=@DocumentType
                      AND BusinessId=@BusinessId AND Status=N'Processing'
                      AND SourceVersion=@SourceVersion;
                    """, connection, transaction);
                complete.Parameters.AddWithValue("@DocumentId", documentId);
                complete.Parameters.AddWithValue("@DocumentType", documentType);
                complete.Parameters.AddWithValue("@BusinessId", businessId);
                complete.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The sales reporting job could not be completed.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                transaction.Rollback("BeforeProjection");
                await using var fail = new SqlCommand("""
                    UPDATE reporting.SalesReportingJobs
                    SET Status=N'Failed',CompletedAt=SYSDATETIMEOFFSET(),LastError=@Error
                    WHERE SourceDocumentId=@DocumentId
                      AND SourceDocumentType=@DocumentType
                      AND BusinessId=@BusinessId AND Status=N'Processing'
                      AND SourceVersion=@SourceVersion;
                    """, connection, transaction);
                fail.Parameters.AddWithValue("@DocumentId", documentId);
                fail.Parameters.AddWithValue("@DocumentType", documentType);
                fail.Parameters.AddWithValue("@BusinessId", businessId);
                fail.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                fail.Parameters.AddWithValue(
                    "@Error", error.Message.Length <= 2000
                        ? error.Message : error.Message[..2000]);
                await fail.ExecuteNonQueryAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ReportingSource?> LockSourceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid documentId, string documentType, Guid businessId,
        long sourceVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(r.SourcePayloadJson,p.PayloadJson),r.Status,r.SourcePayloadHash
            FROM reporting.SalesReportingJobs r WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentProcessingPayloads p
              ON p.DocumentId=r.SourceDocumentId
             AND p.DocumentType=r.SourceDocumentType
             AND p.BusinessId=r.BusinessId
             AND p.PayloadHash=r.SourcePayloadHash
            LEFT JOIN dbo.DocumentProcessingJobs j
              ON j.JobId=r.SourceDocumentProcessingJobId
            WHERE r.SourceDocumentId=@DocumentId
              AND r.SourceDocumentType=@DocumentType
              AND r.BusinessId=@BusinessId AND r.SourceVersion=@SourceVersion
              AND ((r.SourceDocumentProcessingJobId IS NULL AND r.SourcePayloadJson IS NOT NULL)
                OR j.Status=N'Completed');
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SourceVersion", sourceVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var payload=reader.GetString(0);var expected=(byte[])reader[2];
        var actual=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expected,actual))
            throw new InvalidOperationException("The immutable reporting source hash is invalid.");
        return new ReportingSource(payload,reader.GetString(1));
    }

    private sealed record ReportingSource(string Payload, string Status);
}
