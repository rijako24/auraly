using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlSalesReportingJobWriter
{
    public static async Task BackfillServiceInvoicesAsync(SqlConnection connection,SqlTransaction transaction,
        Guid businessId,IAuralyIdGenerator ids,DateTimeOffset now,CancellationToken cancellationToken)
    {
        while(true)
        {
            await using var read=new SqlCommand("""
                SELECT TOP(200) d.DocumentId FROM dbo.SalesDocuments d
                JOIN sales.SalesDocumentServiceFiscalSnapshots s ON s.DocumentId=d.DocumentId
                WHERE d.BusinessId=@Business AND d.DocumentType=N'ServiceInvoice'
                  AND NOT EXISTS(SELECT 1 FROM reporting.SalesReportingJobs r
                    WHERE r.SourceDocumentId=d.DocumentId AND r.SourceDocumentType=d.DocumentType AND r.SourceVersion=1)
                ORDER BY d.DocumentId;
                """,connection,transaction);
            read.Parameters.AddWithValue("@Business",businessId);
            var jobs=new List<object>();
            await using(var reader=await read.ExecuteReaderAsync(cancellationToken))
                while(await reader.ReadAsync(cancellationToken))jobs.Add(new {DocumentId=reader.GetGuid(0),JobId=ids.NewId()});
            if(jobs.Count==0)return;
            await using var write=new SqlCommand("""
                INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
                  SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
                SELECT input.JobId,@Business,d.DocumentId,N'ServiceInvoice',1,s.PayloadHash,s.SnapshotJson,N'Pending',0,@Now
                FROM OPENJSON(@Jobs) WITH(DocumentId uniqueidentifier,JobId uniqueidentifier) input
                JOIN dbo.SalesDocuments d ON d.DocumentId=input.DocumentId AND d.BusinessId=@Business AND d.DocumentType=N'ServiceInvoice'
                JOIN sales.SalesDocumentServiceFiscalSnapshots s ON s.DocumentId=d.DocumentId;
                """,connection,transaction);
            write.Parameters.AddWithValue("@Business",businessId);write.Parameters.AddWithValue("@Now",now);
            write.Parameters.Add("@Jobs",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(jobs);
            if(await write.ExecuteNonQueryAsync(cancellationToken)!=jobs.Count)
                throw new InvalidOperationException("Las fuentes históricas de servicios no se aceptaron completamente.");
        }
    }

    public static async Task InsertServiceInvoiceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid jobId, Guid businessId, Guid documentId,
        string payload, byte[] hash, DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        // Services have no operational job. Their immutable source belongs to the same reporting queue.
        await using var command = new SqlCommand("""
            INSERT reporting.SalesReportingJobs
              (SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
               SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
            VALUES(@Job,@Business,@Document,N'ServiceInvoice',1,@Hash,@Payload,N'Pending',0,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Job", jobId);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@Document", documentId);
        command.Parameters.AddWithValue("@Hash", hash);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", createdAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The service reporting source could not be accepted.");
    }

    public static async Task InsertAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        ConfirmedDocument document,
        IAuralyIdGenerator ids,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT reporting.SalesReportingJobs
              (SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
               SourceVersion,SourcePayloadHash,SourceDocumentProcessingJobId,
               Status,AttemptCount,CreatedAt)
            SELECT @JobId,p.BusinessId,p.DocumentId,p.DocumentType,1,p.PayloadHash,j.JobId,
                   N'Pending',0,@CreatedAt
            FROM dbo.DocumentProcessingPayloads p
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=p.DocumentId AND j.DocumentType=p.DocumentType AND j.BusinessId=p.BusinessId
            WHERE p.DocumentId=@DocumentId AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId
              AND NOT EXISTS
              (
                SELECT 1 FROM reporting.SalesReportingJobs r WITH(UPDLOCK,HOLDLOCK)
                WHERE r.SourceDocumentId=p.DocumentId
                  AND r.SourceDocumentType=p.DocumentType
                  AND r.SourceVersion=1
              );
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@JobId", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", document.DocumentId.Value);
        command.Parameters.AddWithValue("@DocumentType", document.DocumentType);
        command.Parameters.AddWithValue("@BusinessId", document.BusinessId.Value);
        command.Parameters.AddWithValue("@CreatedAt", timeProvider.GetUtcNow());
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted is not (0 or 1))
            throw new InvalidOperationException(
                "An invalid number of sales reporting jobs was created.");
    }
}
