using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlSalesReportingJobWriter
{
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
